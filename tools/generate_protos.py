import argparse
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

DEFAULT_OUTPUT = Path("src/SpiritUnite.Core/Protobuf/pb_protocol.proto")
SOURCE_NAMESPACE = "PbProtocol"
TARGET_NAMESPACE = "SpiritUnite.Packets.Messages"
MAX_ENUM_VALUE = 2**31 - 1
ZIGZAG_FIELD_PREFIXES = {"FrameCmdPkg": "SIntParam"}
OBJECT_MEMBER_NAMES = {"Clone", "Equals", "GetHashCode", "GetType", "MemberwiseClone", "ToString", "WriteTo"}

SCALAR_TYPES = {
    "bool": "bool",
    "ByteString": "bytes",
    "double": "double",
    "float": "float",
    "int": "int32",
    "long": "int64",
    "string": "string",
    "uint": "uint32",
    "ulong": "uint64",
}

NAMESPACE_LINE = re.compile(r"^// Namespace: (.*)$")
TYPE_LINE = re.compile(r"^(?:public|internal) (?:sealed )?(class|enum) ([\w.]+)(?: : (.*?))? // TypeDefIndex")
FIELD_NUMBER_LINE = re.compile(r"^\tpublic const int (\w+)FieldNumber = (\d+);")
PROPERTY_LINE = re.compile(r"^\tpublic ([\w.<>, ]+) (\w+) \{ get;")
ENUM_VALUE_LINE = re.compile(r"^\tpublic const [\w.]+ (\w+) = (-?\d+);")


@dataclass
class Message:
    name: str
    numbers: dict[str, int] = field(default_factory=dict)
    types: dict[str, str] = field(default_factory=dict)


@dataclass
class Enum:
    name: str
    values: dict[str, int] = field(default_factory=dict)


def short_name(name: str) -> str:
    return name.rsplit(".", 1)[-1]


def is_oneof_case(name: str) -> bool:
    return short_name(name).endswith("OneofCase")


def is_message(base_types: str | None, name: str) -> bool:
    return base_types is not None and f"IMessage<{name}>" in base_types


def parse_dump(path: Path) -> tuple[dict[str, Message], dict[str, Enum], dict[str, Enum]]:
    messages: dict[str, Message] = {}
    enums: dict[str, Enum] = {}
    oneofs: dict[str, Enum] = {}
    namespace = ""
    current: Message | Enum | None = None

    with path.open(encoding="utf-8", errors="replace") as dump:
        for line in dump:
            if namespace_match := NAMESPACE_LINE.match(line):
                namespace = namespace_match[1].strip()
            elif type_match := TYPE_LINE.match(line):
                current = open_type(namespace, type_match, messages, enums, oneofs)
            elif line.startswith("}"):
                current = None
            elif isinstance(current, Message):
                read_message_line(current, line)
            elif isinstance(current, Enum) and (value := ENUM_VALUE_LINE.match(line)):
                current.values[value[1]] = int(value[2])

    return messages, enums, oneofs


def open_type(namespace, match, messages, enums, oneofs) -> Message | Enum | None:
    kind, name, base_types = match.groups()
    is_nested_in_message = not namespace and name.split(".")[0] in messages
    if namespace != SOURCE_NAMESPACE and not is_nested_in_message:
        return None
    if kind == "class" and is_message(base_types, name):
        return messages.setdefault(name, Message(name))
    if kind == "enum":
        target = oneofs if is_oneof_case(name) else enums
        return target.setdefault(short_name(name), Enum(short_name(name)))
    return None


def read_message_line(message: Message, line: str) -> None:
    if number := FIELD_NUMBER_LINE.match(line):
        message.numbers[number[1]] = int(number[2])
    elif prop := PROPERTY_LINE.match(line):
        message.types[prop[2]] = prop[1]


def proto_type(csharp_type: str, messages, enums, unresolved: set[str]) -> str:
    if map_match := re.fullmatch(r"MapField<(.+?), (.+)>", csharp_type):
        key = proto_type(map_match[1], messages, enums, unresolved)
        value = proto_type(map_match[2], messages, enums, unresolved)
        return f"map<{key}, {value}>"
    if repeated := re.fullmatch(r"RepeatedField<(.+)>", csharp_type):
        return f"repeated {proto_type(repeated[1], messages, enums, unresolved)}"
    if csharp_type in SCALAR_TYPES:
        return SCALAR_TYPES[csharp_type]

    name = short_name(csharp_type)
    if name in messages or name in enums:
        return name

    unresolved.add(csharp_type)
    return "bytes"


def render_field(message: Message, name: str, messages, enums, unresolved: set[str]) -> str:
    prefix = ZIGZAG_FIELD_PREFIXES.get(message.name)
    if prefix and name.startswith(prefix):
        declared = "sint32"
    else:
        declared = proto_type(message.types[name], messages, enums, unresolved)
    return f"{declared} {csharp_safe_name(name)} = {message.numbers[name]};"


def csharp_safe_name(name: str) -> str:
    return f"{name}Value" if name in OBJECT_MEMBER_NAMES else name


def oneof_members(message: Message, oneofs: dict[str, Enum]) -> dict[str, list[str]]:
    groups: dict[str, list[str]] = {}
    for case in oneofs.values():
        members = [name for name in case.values if name in message.numbers]
        if members:
            groups[case.name.removesuffix("OneofCase")] = members
    return groups


def render_message(message: Message, messages, enums, oneofs, unresolved: set[str]) -> list[str]:
    lines = [f"message {message.name} {{"]
    groups = oneof_members(message, oneofs)
    grouped = {name for members in groups.values() for name in members}

    known = [name for name in message.numbers if name in message.types]
    for name in sorted(known, key=message.numbers.get):
        if name not in grouped:
            lines.append(f"  {render_field(message, name, messages, enums, unresolved)}")

    for group, members in groups.items():
        lines.append(f"  oneof {group} {{")
        for name in sorted(members, key=message.numbers.get):
            lines.append(f"    {render_field(message, name, messages, enums, unresolved)}")
        lines.append("  }")

    lines.append("}")
    return lines


def render_enum(enum: Enum) -> list[str]:
    values = {name: value for name, value in enum.values.items() if abs(value) <= MAX_ENUM_VALUE}
    lines = [f"enum {enum.name} {{"]
    if len(set(values.values())) != len(values):
        lines.append("  option allow_alias = true;")

    entries = sorted(values.items(), key=lambda entry: entry[1] != 0)
    if 0 not in values.values():
        entries.insert(0, ("Unspecified", 0))
    used: set[str] = set()
    for name, value in entries:
        lines.append(f"  {enum.name}_{unique_name(name, used)} = {value};")

    lines.append("}")
    return lines


def unique_name(name: str, used: set[str]) -> str:
    candidate = name
    suffix = 2
    while candidate.casefold() in used:
        candidate = f"{name}_{suffix}"
        suffix += 1
    used.add(candidate.casefold())
    return candidate


def render_proto(messages, enums, oneofs) -> tuple[str, set[str]]:
    unresolved: set[str] = set()
    lines = ['syntax = "proto3";', "", "package pbprotocol;", "", f'option csharp_namespace = "{TARGET_NAMESPACE}";', ""]

    for enum in sorted(enums.values(), key=lambda item: item.name):
        lines += render_enum(enum) + [""]
    for message in sorted(messages.values(), key=lambda item: item.name):
        lines += render_message(message, messages, enums, oneofs, unresolved) + [""]

    return "\n".join(lines), unresolved


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate protobuf definitions from an Il2CppDumper dump.cs")
    parser.add_argument("dump", type=Path, help="dump.cs produced from your own game client")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    return parser.parse_args()


def main() -> int:
    arguments = parse_arguments()
    if not arguments.dump.is_file():
        print(f"dump not found: {arguments.dump}", file=sys.stderr)
        return 1

    messages, enums, oneofs = parse_dump(arguments.dump)
    if not messages:
        print(f"no {SOURCE_NAMESPACE} messages found in {arguments.dump}", file=sys.stderr)
        return 1

    proto, unresolved = render_proto(messages, enums, oneofs)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(proto, encoding="utf-8", newline="\n")

    print(f"{len(messages)} messages and {len(enums)} enums written to {arguments.output}")
    for name in sorted(unresolved):
        print(f"unresolved type written as bytes: {name}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
