#!/usr/bin/env python3
import argparse
import base64
import json
import re
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

DEFAULT_PROTO = Path("src/SpiritUNITE/Protobuf/pb_protocol.proto")
DEFAULT_OUTPUT = Path("src/SpiritUNITE/Data")


@dataclass(frozen=True)
class Field:
    name: str
    type: str
    repeated: bool = False


@dataclass(frozen=True)
class Table:
    message: str
    named_fields: dict[int, Field] | None = None
    key_field: str | None = None
    kept_fields: tuple[str, ...] | None = None

    @property
    def drops_unnamed_fields(self):
        return self.named_fields is not None


LUA_ONLY_FIELDS = {
    "pbLobbyBg": {
        212370: Field("TransmuteId", "int32"),
        250142: Field("LobbyBgID", "int32"),
        266240: Field("OnTime", "int64"),
        280039: Field("IsDefault", "int32"),
        282277: Field("JumpID", "int32"),
        297667: Field("TransmuteNum", "int32"),
    },
    "pbStarRoadSite": {
        225519: Field("SiteType", "int32"),
        240550: Field("NeedScore", "int32"),
        260146: Field("SiteID", "int32"),
        298876: Field("PokemonIDList", "int32", repeated=True),
    },
}

SERVER_FIELD_SUBSETS = {
    "pbActivity_Base": {
        229558: Field("ValidEndTime", "int64"),
        230959: Field("TaskGroupIDList", "int32", repeated=True),
        244760: Field("ActivityID", "int32"),
        256887: Field("ActivityType", "int32"),
        291705: Field("ValidStartTime", "int64"),
        291726: Field("ActivityParam", "int32"),
    },
    "pbTask_Base": {
        227389: Field("ResetType", "int32"),
        251686: Field("GroupID", "int32"),
        256687: Field("SDropID", "int32"),
        266136: Field("FinishCondition", "pbFinishCondition", repeated=True),
        289808: Field("TaskID", "int32"),
    },
}

NESTED_MESSAGE_FIELDS = {
    "pbFinishCondition": {
        266127: Field("ID", "int32"),
        288334: Field("TargetProcess", "int32"),
    },
}

SHOP_SERVER_FIELDS = (
    "ShopID",
    "ShopType",
    "ShopResID",
    "ABTestID",
    "OnTime",
    "OffTime",
    "IsPreView",
    "PreViewStartTime",
    "PreViewEndTime",
    "Level",
    "BuyLimitType",
    "BuyLimitNum",
    "CanBulkBuy",
    "IsCookiesBuy",
    "CookiesBuy",
    "DiscountCookiesBuy",
    "IsPokeCoinsBuy",
    "PokeCoinsBuy",
    "DiscountPokeCoinsBuy",
    "IsCouponsBuy",
    "CouponsBuy",
    "DiscountCouponsBuy",
    "IsCandysBuy",
    "IsFramentBuy",
    "IsCommonBuy",
    "IsBuyMixPay",
    "DiscountStartTime",
    "DiscountEndTime",
    "SubShopItemIDs",
)

OUT_SIDE_ITEM_SERVER_FIELDS = ("OutSideItemId", "OutSideItemType", "ItemParam")

TABLES = {
    "databin_ActivityTask": Table("pbTask_Base", SERVER_FIELD_SUBSETS["pbTask_Base"], key_field="TaskID"),
    "databin_Activity_Base": Table(
        "pbActivity_Base", SERVER_FIELD_SUBSETS["pbActivity_Base"], key_field="ActivityID"
    ),
    "databin_AvatarItem": Table("pbAvatarItem", key_field="AvatarItemId"),
    "databin_AvatarSuit": Table("pbAvatarSuit", key_field="AvatarSuitId"),
    "databin_CreateRoleShowAvatar": Table("pbCreateRoleShowAvatar"),
    "databin_DefaultAvatarItem": Table("pbDefaultAvatarItem"),
    "databin_Drop_Static": Table("pbDrop_Static"),
    "databin_LobbyBg": Table("pbLobbyBg", LUA_ONLY_FIELDS["pbLobbyBg"]),
    "databin_Newbie_RewardHero": Table("pbNewbie_RewardHero"),
    "databin_OutSideItem_Base": Table(
        "pbOutSideItem_Base", key_field="OutSideItemId", kept_fields=OUT_SIDE_ITEM_SERVER_FIELDS
    ),
    "databin_RankedMatchSeason": Table("pbRankedMatchSeason", key_field="SeasonId"),
    "databin_RoleImage": Table("pbRoleImage"),
    "databin_Shop.group.1": Table("pbShop", key_field="ShopID", kept_fields=SHOP_SERVER_FIELDS),
    "databin_StarRoadSite": Table("pbStarRoadSite", LUA_ONLY_FIELDS["pbStarRoadSite"]),
}

TABLE_MAGIC = b"pbc"
SUPPORTED_TABLE_VERSIONS = (1, 2)
INDEXED_TABLE_VERSION = 2
TABLE_HEADER = struct.Struct("<3sBII")
ROW_INDEX_ENTRY = struct.Struct("<QI")
UINT32 = struct.Struct("<I")
SECONDARY_INDEX_VALUE = struct.Struct("<iI")
ROW_KEY = struct.Struct("<Q")

WIRE_VARINT = 0
WIRE_FIXED64 = 1
WIRE_LENGTH_DELIMITED = 2
WIRE_FIXED32 = 5
MAX_VARINT_SHIFT = 63

VARINT_TYPES = {"int32", "int64", "uint32", "uint64", "bool"}
FIXED_WIDTH_TYPES = {
    "fixed64": struct.Struct("<Q"),
    "sfixed64": struct.Struct("<q"),
    "double": struct.Struct("<d"),
    "fixed32": struct.Struct("<I"),
    "sfixed32": struct.Struct("<i"),
    "float": struct.Struct("<f"),
}
WIRE_TYPE_OF_FIXED_WIDTH = {struct.calcsize("<Q"): WIRE_FIXED64, struct.calcsize("<I"): WIRE_FIXED32}
SIGNED_VARINT_TYPES = {"int32", "int64"}
JSON_STRING_TYPES = {"int64", "uint64", "fixed64", "sfixed64"}
UINT64_RANGE = 1 << 64
INT32_RANGE = range(-(1 << 31), 1 << 31)

PROTO_MESSAGE = re.compile(r"^message (\w+) \{\n(.*?)^\}", re.M | re.S)
PROTO_ENUM = re.compile(r"^enum (\w+) \{", re.M)
PROTO_FIELD = re.compile(r"^\s*(repeated\s+)?([\w.]+)\s+(\w+)\s*=\s*(\d+);", re.M)


class ConversionError(Exception):
    pass


class ByteReader:
    def __init__(self, data, offset=0, end=None):
        self.data = data
        self.offset = offset
        self.end = len(data) if end is None else end

    @property
    def at_end(self):
        return self.offset >= self.end

    def unpack(self, layout, what):
        if self.offset + layout.size > self.end:
            raise ConversionError(f"{what} runs past the end")
        values = layout.unpack_from(self.data, self.offset)
        self.offset += layout.size
        return values

    def take(self, length, what):
        if self.offset + length > self.end:
            raise ConversionError(f"{what} runs past the end")
        chunk = self.data[self.offset : self.offset + length]
        self.offset += length
        return chunk

    def varint(self):
        result = shift = 0
        while True:
            if self.at_end or shift > MAX_VARINT_SHIFT:
                raise ConversionError("truncated varint")
            byte = self.data[self.offset]
            self.offset += 1
            result |= (byte & 0x7F) << shift
            shift += 7
            if byte < 0x80:
                return result


def read_proto_messages(proto_path):
    text = proto_path.read_text(encoding="utf-8")
    enum_names = set(PROTO_ENUM.findall(text))
    return {
        message_name: {
            int(number): Field(name, "int32" if field_type in enum_names else field_type, bool(repeated))
            for repeated, field_type, name, number in PROTO_FIELD.findall(body)
        }
        for message_name, body in PROTO_MESSAGE.findall(text)
    }


def read_row_index(reader):
    magic, version, _, row_count = reader.unpack(TABLE_HEADER, "table header")
    if magic != TABLE_MAGIC:
        raise ConversionError("missing pbc table header")
    if version not in SUPPORTED_TABLE_VERSIONS:
        raise ConversionError(f"table version {version} is not supported")
    row_index = [reader.unpack(ROW_INDEX_ENTRY, "row index") for _ in range(row_count)]
    if len({key for key, _ in row_index}) != row_count:
        raise ConversionError("row index repeats a key")
    return version, row_index


def skip_secondary_indexes(reader, row_keys):
    (index_count,) = reader.unpack(UINT32, "secondary index count")
    for _ in range(index_count):
        (name_length,) = reader.unpack(UINT32, "secondary index name length")
        reader.take(name_length, "secondary index name")
        (value_count,) = reader.unpack(UINT32, "secondary index value count")
        indexed_key_count = 0
        for _ in range(value_count):
            _, key_count = reader.unpack(SECONDARY_INDEX_VALUE, "secondary index value")
            for _ in range(key_count):
                (key,) = reader.unpack(ROW_KEY, "secondary index key")
                if key not in row_keys:
                    raise ConversionError(f"secondary index names key {key}, which has no row")
            indexed_key_count += key_count
        if indexed_key_count != len(row_keys):
            raise ConversionError(
                f"secondary index covers {indexed_key_count} keys, not the table's {len(row_keys)} rows"
            )


def read_rows(table_bytes):
    reader = ByteReader(table_bytes)
    version, row_index = read_row_index(reader)
    if version == INDEXED_TABLE_VERSION:
        skip_secondary_indexes(reader, {key for key, _ in row_index})
    rows_start = reader.offset
    rows_length = len(table_bytes) - rows_start
    row_starts = [start for _, start in row_index]
    row_ends = row_starts[1:] + [rows_length]
    for (key, start), end in zip(row_index, row_ends):
        if not 0 <= start <= end <= rows_length:
            raise ConversionError("row offsets are out of order or out of range")
        yield key, table_bytes[rows_start + start : rows_start + end]


def decode_varint(raw_value, field):
    if field.type == "bool":
        return raw_value != 0
    value = raw_value
    if field.type in SIGNED_VARINT_TYPES and value >= UINT64_RANGE >> 1:
        value -= UINT64_RANGE
    if field.type == "int32" and value not in INT32_RANGE:
        raise ConversionError(f"value {value} does not fit int32")
    return value


def decode_packed(payload, field):
    if field.type in VARINT_TYPES:
        reader = ByteReader(payload)
        values = []
        while not reader.at_end:
            values.append(decode_varint(reader.varint(), field))
        return values
    layout = FIXED_WIDTH_TYPES[field.type]
    if len(payload) % layout.size:
        raise ConversionError(f"{field.name} packed payload is truncated")
    return [value for (value,) in layout.iter_unpack(payload)]


def decode_length_delimited(payload, field):
    if field.type in NESTED_MESSAGE_FIELDS:
        return [convert_row(payload, NESTED_MESSAGE_FIELDS[field.type], drops_unnamed_fields=True)]
    if field.type == "string":
        try:
            return [payload.decode("utf-8")]
        except UnicodeDecodeError as error:
            raise ConversionError(f"{field.name} is not valid UTF-8") from error
    if field.type == "bytes":
        return [base64.b64encode(payload).decode("ascii")]
    if field.repeated and (field.type in VARINT_TYPES or field.type in FIXED_WIDTH_TYPES):
        return decode_packed(payload, field)
    raise ConversionError(f"{field.name} has type {field.type}, which this converter cannot write")


def decode_field_values(reader, wire_type, field):
    if wire_type == WIRE_VARINT and field.type in VARINT_TYPES:
        return [decode_varint(reader.varint(), field)]
    layout = FIXED_WIDTH_TYPES.get(field.type)
    if layout is not None and wire_type == WIRE_TYPE_OF_FIXED_WIDTH[layout.size]:
        return list(reader.unpack(layout, field.name))
    if wire_type == WIRE_LENGTH_DELIMITED:
        return decode_length_delimited(reader.take(reader.varint(), field.name), field)
    raise ConversionError(f"{field.name} has wire type {wire_type}, which does not match its type {field.type}")


def skip_field_value(reader, wire_type):
    if wire_type == WIRE_VARINT:
        reader.varint()
    elif wire_type == WIRE_FIXED64:
        reader.offset += 8
    elif wire_type == WIRE_FIXED32:
        reader.offset += 4
    elif wire_type == WIRE_LENGTH_DELIMITED:
        reader.take(reader.varint(), "skipped field")
    else:
        raise ConversionError(f"unsupported wire type {wire_type}")


def to_json_value(value, field):
    return str(value) if field.type in JSON_STRING_TYPES else value


def convert_row(row_bytes, fields, drops_unnamed_fields):
    reader = ByteReader(row_bytes)
    values = {}
    while not reader.at_end:
        tag = reader.varint()
        number, wire_type = tag >> 3, tag & 7
        field = fields.get(number)
        if field is None:
            if not drops_unnamed_fields:
                raise ConversionError(f"field {number} is not in the message schema")
            skip_field_value(reader, wire_type)
            continue
        decoded = decode_field_values(reader, wire_type, field)
        if field.repeated:
            values.setdefault(number, []).extend(decoded)
        elif number in values:
            raise ConversionError(f"{field.name} appears twice but is not repeated")
        else:
            values[number] = decoded[0]
    if reader.offset != len(row_bytes):
        raise ConversionError("row ends inside a field")
    return {fields[number].name: to_json_values(value, fields[number]) for number, value in sorted(values.items())}


def to_json_values(value, field):
    if field.repeated:
        return [to_json_value(item, field) for item in value]
    return to_json_value(value, field)


def fields_of(table, proto_messages):
    if table.named_fields is not None:
        return table.named_fields
    if table.message in proto_messages:
        return proto_messages[table.message]
    raise ConversionError(f"{table.message} is not in the proto file")


def check_kept_fields(table, fields):
    field_names = {field.name for field in fields.values()}
    missing = [name for name in table.kept_fields or () if name not in field_names]
    if missing:
        raise ConversionError(f"kept fields are not in {table.message}: {', '.join(missing)}")


def check_row_key(table, row, index_key):
    if table.key_field is not None and str(row.get(table.key_field, 0)) != str(index_key):
        raise ConversionError(f"row with index key {index_key} has {table.key_field} {row.get(table.key_field)}")


def keep_server_fields(table, row):
    if table.kept_fields is None:
        return row
    return {name: value for name, value in row.items() if name in table.kept_fields}


def convert_table(databin_name, table, input_folder, proto_messages):
    fields = fields_of(table, proto_messages)
    table_path = input_folder / f"{databin_name}.bin"
    if not table_path.is_file():
        raise ConversionError(f"{table_path} does not exist; decode {databin_name} first")
    check_kept_fields(table, fields)
    rows = []
    for index_key, row_bytes in read_rows(table_path.read_bytes()):
        row = convert_row(row_bytes, fields, table.drops_unnamed_fields)
        check_row_key(table, row, index_key)
        rows.append(keep_server_fields(table, row))
    if not rows:
        raise ConversionError("table has no rows")
    return rows


def write_rows(output_path, rows):
    with output_path.open("w", encoding="utf-8", newline="\n") as output:
        json.dump(rows, output, ensure_ascii=False, indent=2)
        output.write("\n")


def parse_arguments():
    parser = argparse.ArgumentParser(description="Convert decoded databin tables into the server's game data files.")
    parser.add_argument("names", nargs="*", help="Databin names to convert; all known tables when omitted")
    parser.add_argument("--input", type=Path, required=True, help="Folder holding decoded databin_X.bin tables")
    parser.add_argument("--proto", type=Path, default=DEFAULT_PROTO)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    args.names = args.names or sorted(TABLES)
    unknown = [name for name in args.names if name not in TABLES]
    if unknown:
        parser.error(f"Unknown databin tables: {', '.join(unknown)}; add them to TABLES first")
    if not args.input.is_dir():
        parser.error(f"Input folder does not exist: {args.input}")
    if not args.proto.is_file():
        parser.error(f"Proto file does not exist: {args.proto}; run scripts/generate_protos.py first")
    return args


def main():
    args = parse_arguments()
    proto_messages = read_proto_messages(args.proto)
    args.output.mkdir(parents=True, exist_ok=True)
    failures = 0
    for name in args.names:
        table = TABLES[name]
        try:
            rows = convert_table(name, table, args.input, proto_messages)
        except (ConversionError, struct.error) as error:
            print(f"{name}: {error}", file=sys.stderr)
            failures += 1
            continue
        output_path = args.output / f"{table.message}.json"
        write_rows(output_path, rows)
        print(f"{name}: {len(rows)} rows written to {output_path}")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
