import argparse
import importlib.util
import json
import re
import sys
from pathlib import Path

DATABIN_CONVERTER = Path(__file__).with_name("convert_databins.py")
converter_spec = importlib.util.spec_from_file_location("convert_databins", DATABIN_CONVERTER)
databins = importlib.util.module_from_spec(converter_spec)
converter_spec.loader.exec_module(databins)
ConversionError = databins.ConversionError
Field = databins.Field

DEFAULT_OUTPUT = Path("src/SpiritUnite.Core/Databin/Names/en.json")
GARBLED_POKEMON = "Pok�mon"
POKEMON_WORD = "Pokémon"
REPLACEMENT_CHARACTER = "�"
PLURAL_CHOICE = re.compile(r"<\d+\.\d+:\x08([^\n\x00]*)\n[^\x00]*\x00\x00>")
PARAMETER = re.compile(r"<\d+\.\d+:[^>]*>")
NONBREAKING_SPACE = re.compile(r"<\d+\.\d+>")
BRACKET_START_SPACE = re.compile(r"\(\s+")
MARKUP = re.compile(r"</?[a-zA-Z][^>]*>")
SPACES = re.compile(r"\s+")
DURATION_PLACEHOLDER = re.compile(r"\{\d+\}-Day\s*")
VALUE_PLACEHOLDER = re.compile(r"\{\d+\}\S*\s*")
EMPTY_LIST_START = re.compile(r"\(\s*,\s*")
EMPTY_LIST_END = re.compile(r"\s*,\s*\)")
EMPTY_BRACKETS = re.compile(r"\s*\(\s*\)")
JOINED_WORDS = re.compile(r"(?<=[a-z])(?=[A-Z])")
EMBLEM_DEX = re.compile(r"mat_badge_(\d+)_")
NAME_IN_KEY = re.compile(r"Pkm_Name_([A-Za-z]+?)(?:_\d+)?$")
DEX_NAME_KEY = "pkm_name_tag_{:03d}"


class NameSource:
    def __init__(self, databin, message, key_field, name_fields, extra_fields=(), lua_fields=None, raw_name=False, resolver=None):
        self.databin = databin
        self.message = message
        self.key_field = key_field
        self.name_fields = name_fields
        self.extra_fields = extra_fields
        self.lua_fields = lua_fields
        self.raw_name = raw_name
        self.resolver = resolver


SOURCES = {
    "Pokemon": NameSource("databin_Pokemon_Base", "pbPokemon_Base", "PokemonId", ("EnglishName",), raw_name=True),
    "OutSideItem": NameSource("databin_OutSideItem_Base", "pbOutSideItem_Base", "OutSideItemId", ("OutSideItemName",)),
    "AvatarItem": NameSource("databin_AvatarItem", "pbAvatarItem", "AvatarItemId", ("AvatarName", "AvatarNameFemale", "AvatarNameMale")),
    "PokemonAvatar": NameSource(
        "databin_Pokemon_Avatar_Base", "pbPokemon_Avatar_Base", "PokemonAvatarId", ("PokemonAvatarName",), extra_fields=("PokemonId",)
    ),
    "HeadIcon": NameSource("databin_HeadIcon_Base", "pbHeadIcon_Base", "HeadIconID", ("HeadIconName",)),
    "HeadFrame": NameSource(
        "databin_PersonStyleHeadFrame",
        "pbPersonStyleHeadFrame",
        "HeadIconID",
        ("HeadIconName",),
        lua_fields={**databins.LUA_ONLY_FIELDS["pbPersonStyleHeadFrame"], 299108: Field("HeadIconName", "string")},
    ),
    "LobbyBg": NameSource(
        "databin_LobbyBg",
        "pbLobbyBg",
        "LobbyBgID",
        ("LobbyBgName",),
        lua_fields={**databins.LUA_ONLY_FIELDS["pbLobbyBg"], 227312: Field("LobbyBgName", "string")},
    ),
    "SceneMap": NameSource("databin_Scene_Map", "pbScene_Map", "SceneId", ("Remarks",)),
    "Badge": NameSource("databin_Badge_Base", "pbBadge_Base", "BadgeId", ("BadgeName",), extra_fields=("BadgeMat",), resolver="emblem"),
}


def clean(text, parameter):
    text = text.replace(GARBLED_POKEMON, POKEMON_WORD).replace(REPLACEMENT_CHARACTER, "")
    text = PLURAL_CHOICE.sub(lambda match: match.group(1), text)
    text = NONBREAKING_SPACE.sub(" ", text)
    text = PARAMETER.sub(parameter or "", text)
    text = MARKUP.sub("", text)
    text = DURATION_PLACEHOLDER.sub("", text)
    text = VALUE_PLACEHOLDER.sub("", text)
    text = EMPTY_LIST_START.sub("(", text)
    text = EMPTY_LIST_END.sub(")", text)
    text = EMPTY_BRACKETS.sub("", text)
    text = BRACKET_START_SPACE.sub("(", text)
    return SPACES.sub(" ", text).strip(" :")


def split_joined_words(name):
    return JOINED_WORDS.sub(" ", name)


def read_table(source, input_folder, proto_messages):
    all_fields = source.lua_fields if source.lua_fields is not None else proto_messages.get(source.message)
    if all_fields is None:
        raise ConversionError(f"{source.message} is not in the proto file")
    wanted = {source.key_field, *source.name_fields, *source.extra_fields}
    fields = {number: field for number, field in all_fields.items() if field.name in wanted}
    table_path = input_folder / f"{source.databin}.bin"
    if not table_path.is_file():
        raise ConversionError(f"{table_path} does not exist; decode {source.databin} first")
    return [databins.convert_row(row_bytes, fields, True) for _, row_bytes in databins.read_rows(table_path.read_bytes())]


def emblem_name(row, english):
    dex_match = EMBLEM_DEX.match(row.get("BadgeMat") or "")
    dex = int(dex_match.group(1)) if dex_match else None
    if dex is not None and english.get(DEX_NAME_KEY.format(dex)):
        return clean(english[DEX_NAME_KEY.format(dex)], None)
    if key_match := NAME_IN_KEY.match(row.get("BadgeName") or ""):
        return split_joined_words(key_match.group(1))
    return f"No. {dex}" if dex is not None else None


def name_of(row, source, english, parameter):
    if source.resolver == "emblem":
        text = english.get(row.get("BadgeName") or "")
        return clean(text, None) if text else emblem_name(row, english)
    for field in source.name_fields:
        value = row.get(field)
        if not value:
            continue
        text = split_joined_words(value) if source.raw_name else english.get(value, "")
        cleaned = clean(text, parameter) if text else ""
        if cleaned:
            return cleaned
    return None


def build_names(input_folder, english, proto_messages):
    names = {}
    pokemon_names = {}
    for group, source in SOURCES.items():
        found = {}
        for row in read_table(source, input_folder, proto_messages):
            key = row.get(source.key_field)
            if not key:
                continue
            parameter = pokemon_names.get(str(row.get("PokemonId"))) if "PokemonId" in source.extra_fields else None
            name = name_of(row, source, english, parameter)
            if name:
                found[str(key)] = name
        names[group] = dict(sorted(found.items(), key=lambda item: int(item[0])))
        if group == "Pokemon":
            pokemon_names = names[group]
        print(f"{group}: {len(found)} names")
    return names


def parse_arguments():
    parser = argparse.ArgumentParser(description="Write the English names of client items, Pokemon, cosmetics and maps for the admin panel.")
    parser.add_argument("--input", type=Path, required=True, help="Folder holding decoded databin_X.bin tables")
    parser.add_argument("--text", type=Path, required=True, help="The client's English text as one JSON object of text key to text")
    parser.add_argument("--proto", type=Path, default=databins.DEFAULT_PROTO)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    if not args.input.is_dir():
        parser.error(f"Input folder does not exist: {args.input}")
    if not args.text.is_file():
        parser.error(f"English text file does not exist: {args.text}")
    if not args.proto.is_file():
        parser.error(f"Proto file does not exist: {args.proto}; run tools/generate_protos.py first")
    return args


def main():
    args = parse_arguments()
    english = json.loads(args.text.read_text(encoding="utf-8"))
    try:
        names = build_names(args.input, english, databins.read_proto_messages(args.proto))
    except ConversionError as error:
        print(error, file=sys.stderr)
        return 1
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8", newline="\n") as output:
        json.dump(names, output, ensure_ascii=False, indent=2)
        output.write("\n")
    print(f"Names written to {args.output}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
