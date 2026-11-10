import argparse
import hashlib
import ipaddress
import json
import lzma
import os
import re
import secrets
import shutil
import socket
import struct
import subprocess
import sys
import tempfile
import urllib.request
import zipfile
import zlib
from collections.abc import Callable
from dataclasses import dataclass, field
from pathlib import Path
from xml.etree import ElementTree

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
APPSETTINGS_PATH = REPOSITORY_ROOT / "src/SpiritUnite.Core/appsettings.json"
DEFAULT_WORK_DIR = REPOSITORY_ROOT / "apk-build"

BASE_APK = "base.apk"
ASSET_PACK_APK = "split_ab.apk"
NATIVE_APK = "split_config.arm64_v8a.apk"
SPLIT_APKS = (BASE_APK, ASSET_PACK_APK, NATIVE_APK)

MSDK_CONFIG_ENTRY = "assets/MSDKConfig.ini"
CENTAURI_CONFIG_ENTRY = "assets/centauri_oversea_singapore/centauri_oversea_cp.cfg"
RES_DATA_ENTRY = "assets/res.data"
RESOURCES_ARCHIVE_ENTRY = "lib/arm64-v8a/libResources.so"
METADATA_ARCHIVE_PATH = "Managed/Metadata/global-metadata.dat"
MANIFEST_ENTRY = "AndroidManifest.xml"
SOURCE_STAMP_ENTRY = "stamp-cert-sha256"
SPLIT_ONLY_ENTRIES = frozenset({MANIFEST_ENTRY, SOURCE_STAMP_ENTRY})
RESOURCE_TABLE_ENTRY = "resources.arsc"
SHELL_DEX_ENTRY = "classes2.dex"
SHELL_DEX_SHA256 = "0424acd386cc14745c209ccae1662259777889b026c04f60e71953855095d7d7"
ATTACH_BASE_CONTEXT_SHELL_INSTRUCTIONS = 0x3081B8
GCLOUD_LIBRARY_ENTRY = "lib/arm64-v8a/libgcloud.so"
GCLOUD_LIBRARY_SHA256 = "f2932b84be1cbf5f59eb8126ad8427428c76990d21b76f8d927f78e2f87b3ce0"
IS_DOMAIN_NAME_RETURNS_DOMAIN = 0x208748
MOV_W20_ONE = 0x52800034
MOV_W20_ZERO = 0x52800014

AXML_STRING_POOL = 0x0001
AXML_START_ELEMENT = 0x0102
AXML_UTF8_FLAG = 0x100
AXML_TYPE_STRING = 0x03
AXML_TYPE_BOOLEAN = 0x12
AXML_TRUE = 0xFFFFFFFF
AXML_STRING_POOL_HEADER_SIZE = 28
RESOURCE_TABLE_TYPE = 0x0002
RESOURCE_TABLE_PACKAGE_TYPE = 0x0200
RESOURCE_TABLE_PACKAGE_NAME_UNITS = 128
APPLICATION_ELEMENT = "application"
MANIFEST_ELEMENT = "manifest"
PACKAGE_ATTRIBUTE = "package"
LABEL_ATTRIBUTE = "label"
LABELLED_ELEMENTS = ("application", "activity", "activity-alias")
OFFICIAL_PACKAGE_NAME = "jp.pokemon.pokemonunite"
PACKAGE_NAME_PREFIX = "org.spiritunite"
DEVELOPMENT_PACKAGE_SEGMENT = "dev"
PACKAGE_NAME_SEGMENT = re.compile(r"^[A-Za-z][A-Za-z0-9_]*$")
MAX_PACKAGE_NAME_LENGTH = RESOURCE_TABLE_PACKAGE_NAME_UNITS - 1
DEFAULT_APP_LABEL = "SpiritUNITE"
MAX_APP_LABEL_LENGTH = 50
NETWORK_SECURITY_CONFIG_ATTRIBUTE = "networkSecurityConfig"
USES_CLEARTEXT_TRAFFIC_ATTRIBUTE = "usesCleartextTraffic"
REQUIRED_SPLIT_TYPES_ATTRIBUTE = "requiredSplitTypes"

DEFAULT_DIR_PORT = 10001
DEFAULT_HTTPS_PORT = 443
DEFAULT_HTTP_PORT = 80
DEFAULT_DEV_WEB_PORT = 10081
WEBVIEW_REFUSED_PORTS = (10080,)
DEFAULT_LOBBY_PORT = 10002
DEFAULT_VOICE_PORT = 8700
GVOICE_SERVER_SCHEME = "udp://"
DIR_NODE_NAME = "trunk_global_dist"
CLOUD_CONTROL_REPLACEMENT_HOST = "spiritunite.invalid"
UNREACHABLE_ORIGIN = "https://spiritunite.invalid"
POKEMON_REDIRECT_URI = f"{UNREACHABLE_ORIGIN}/pokemon/loginSuccess"
NINTENDO_REDIRECT_URI = f"{UNREACHABLE_ORIGIN}/nintendo/loginSuccess"
SILENCED_MSDK_HOST_KEYS = (
    "MSDK_ACCOUNT_URL",
    "XG_HOST",
    "BUGLY_REPORT_URL_ANDROID",
    "BUGLY_REPORT_URL_IOS",
    "TWITTER_WEB_LOGIN_URL",
)
IMAGE_CDN_ROUTE = "/cdn"
CENTAURI_RELEASE_MODE = "release"

SIGNING_KEY_ALIAS = "spiritunite"
SIGNING_KEY_DNAME = "CN=SpiritUNITE"
SIGNING_KEY_VALIDITY_DAYS = 36500

ANDROID_REPOSITORY_URL = "https://dl.google.com/android/repository/"
ANDROID_REPOSITORY_INDEX = ANDROID_REPOSITORY_URL + "repository2-3.xml"
ANDROID_SDK_LICENSE_URL = "https://developer.android.com/studio/terms"

ZIP_LOCAL_HEADER = struct.Struct("<IHHHHHIIIHH")
ZIP_CENTRAL_HEADER = struct.Struct("<IHHHHHHIIIHHHHHII")
ZIP_END_OF_CENTRAL_DIRECTORY = struct.Struct("<IHHHHIIH")
ZIP_LOCAL_SIGNATURE = 0x04034B50
ZIP_CENTRAL_SIGNATURE = 0x02014B50
ZIP_END_SIGNATURE = 0x06054B50
ZIP_DATA_DESCRIPTOR_FLAG = 0x08
ZIP_STORED = 0
ZIP_DEFLATED = 8
ANDROID_ALIGNMENT_EXTRA_ID = 0xD935
ANDROID_ALIGNMENT_EXTRA_MIN_SIZE = 6
STORED_ENTRY_ALIGNMENT = 4
NATIVE_LIBRARY_ALIGNMENT = 16384
V1_SIGNATURE_FILE = re.compile(
    r"^META-INF/([^/]+\.(SF|RSA|DSA|EC)|MANIFEST\.MF)$", re.IGNORECASE
)
COPY_CHUNK_SIZE = 8 * 1024 * 1024

SEVEN_ZIP_SIGNATURE = b"7z\xbc\xaf\x27\x1c"
SEVEN_ZIP_VERSION = b"\x00\x04"
SEVEN_ZIP_START_HEADER_SIZE = 32
SEVEN_ZIP_LZMA_CODER = b"\x03\x01\x01"
SEVEN_ZIP_LZMA2_CODER = b"\x21"
LZMA2_PRESET = 6


class SevenZipId:
    END = 0x00
    HEADER = 0x01
    ARCHIVE_PROPERTIES = 0x02
    ADDITIONAL_STREAMS_INFO = 0x03
    MAIN_STREAMS_INFO = 0x04
    FILES_INFO = 0x05
    PACK_INFO = 0x06
    UNPACK_INFO = 0x07
    SUBSTREAMS_INFO = 0x08
    SIZE = 0x09
    CRC = 0x0A
    FOLDER = 0x0B
    CODERS_UNPACK_SIZE = 0x0C
    NUM_UNPACK_STREAM = 0x0D
    EMPTY_STREAM = 0x0E
    NAME = 0x11
    ENCODED_HEADER = 0x17


class BuildError(Exception):
    pass


@dataclass(frozen=True)
class MetadataLiteral:
    entry_offset: int
    original: str


@dataclass(frozen=True)
class ClientBuild:
    version_code: int
    string_literal_data_offset: int
    dir_address: MetadataLiteral
    cloud_control_host: MetadataLiteral
    image_cdn_address: MetadataLiteral
    gvoice_server_url: MetadataLiteral
    update_hosts: tuple[MetadataLiteral, ...]


CLIENT_BUILDS_BY_METADATA_MD5 = {
    "22a544b07aa748118d7bae025dd3fbef": ClientBuild(
        version_code=1192039,
        string_literal_data_offset=0x53C10,
        dir_address=MetadataLiteral(
            0x16238, "trunk_global_dist|dirsvr.pokemon-unitepgame.com:10001"
        ),
        cloud_control_host=MetadataLiteral(0x5768, "gcloudctrl.iegcom.com"),
        image_cdn_address=MetadataLiteral(
            0x3F730, "https://image.pokemon-unitepgame.com/Encrypt"
        ),
        gvoice_server_url=MetadataLiteral(0x11380, "udp://sg.voice.gcloudcs.com:8700"),
        update_hosts=(
            MetadataLiteral(0x3E8F8, "puffer.6.669535640.nishikigcloud.iegcom.com"),
            MetadataLiteral(
                0x3E8F0, "pre-puffer.5.669535640.nishikigcloud.iegcom.com"
            ),
            MetadataLiteral(
                0x50A68, "download.2.669535640.nishikigcloud.iegcom.com"
            ),
            MetadataLiteral(
                0x50A60, "pre-download.1.669535640.nishikigcloud.iegcom.com"
            ),
        ),
    ),
}

CLIENT_ADDRESS_SERVICE_PORTS = (8085, 8080)

PENDING_CLIENT_CHANGES = (
    "TDM, qqcollect and pokemon-webapi telemetry hosts are compiled into native code and still contacted.",
    "The client still resolves gcloudctrl.iegcom.com at startup; a resources.arsc string names it.",
)


@dataclass(frozen=True)
class HostTarget:
    host: str
    dir_port: int
    lobby_port: int
    web_port: int
    update_port: int
    voice_host: str
    voice_port: int
    development: bool
    package_name: str
    app_label: str

    @property
    def web_origin(self) -> str:
        scheme, default_port = (
            ("http", DEFAULT_HTTP_PORT)
            if self.development
            else ("https", DEFAULT_HTTPS_PORT)
        )
        if self.web_port == default_port:
            return f"{scheme}://{self.host}"
        return f"{scheme}://{self.host}:{self.web_port}"

    @property
    def image_cdn_address(self) -> str:
        return f"{self.web_origin}{IMAGE_CDN_ROUTE}"

    @property
    def build_name(self) -> str:
        return f"dev-{self.host}" if self.development else self.host

    @property
    def dir_address(self) -> str:
        return f"{DIR_NODE_NAME}|{self.host}:{self.dir_port}"

    @property
    def gvoice_server_url(self) -> str:
        return f"{GVOICE_SERVER_SCHEME}{self.voice_host}:{self.voice_port}"


@dataclass(frozen=True)
class SigningTools:
    java: Path
    keytool: Path
    apksigner_jar: Path


def log(message: str) -> None:
    print(message, flush=True)


def is_ipv4_literal(host: str) -> bool:
    try:
        return isinstance(ipaddress.ip_address(host), ipaddress.IPv4Address)
    except ValueError:
        return False


def is_loopback(host: str) -> bool:
    if host.lower() == "localhost":
        return True
    try:
        return ipaddress.ip_address(host).is_loopback
    except ValueError:
        return False


HOSTNAME_LABEL = re.compile(r"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)$")


def validate_host(host: str) -> str:
    host = host.strip().rstrip(".")
    if not host:
        raise BuildError(
            "The server address is empty. Pass --host with your domain or public IPv4 address."
        )
    if "://" in host or "/" in host:
        raise BuildError(
            f"--host must be a bare domain or IPv4 address, not a URL: {host}"
        )
    try:
        address = ipaddress.ip_address(host)
    except ValueError:
        address = None
    if isinstance(address, ipaddress.IPv6Address):
        raise BuildError(
            "IPv6 addresses are not supported: the client splits the directory address on ':'. Use a domain or IPv4 address."
        )
    if address is None and (
        len(host) > 253
        or not all(HOSTNAME_LABEL.match(label) for label in host.split("."))
    ):
        raise BuildError(f"--host is not a valid domain name or IPv4 address: {host}")
    return host


def validate_port(value: int, option: str) -> int:
    if not 1 <= value <= 65535:
        raise BuildError(f"{option} must be between 1 and 65535, got {value}.")
    return value


def validate_package_name(package_name: str) -> str:
    segments = package_name.split(".")
    if (
        len(segments) < 2
        or not all(PACKAGE_NAME_SEGMENT.match(segment) for segment in segments)
        or len(package_name) > MAX_PACKAGE_NAME_LENGTH
    ):
        raise BuildError(
            f"{package_name!r} is not a valid Android package name: use at least two dot-separated parts of letters, digits and underscores, each starting with a letter, at most {MAX_PACKAGE_NAME_LENGTH} characters."
        )
    if package_name == OFFICIAL_PACKAGE_NAME:
        raise BuildError(
            f"--package {OFFICIAL_PACKAGE_NAME} is the official app's name; the build would replace the official app."
        )
    return package_name


def default_package_name(host: str, development: bool) -> str:
    host_segment = re.sub(r"[^A-Za-z0-9]", "_", host.lower())
    if not host_segment[0].isalpha():
        host_segment = f"h{host_segment}"
    segments = [PACKAGE_NAME_PREFIX]
    if development:
        segments.append(DEVELOPMENT_PACKAGE_SEGMENT)
    segments.append(host_segment)
    package_name = ".".join(segments)
    if len(package_name) > MAX_PACKAGE_NAME_LENGTH:
        raise BuildError(
            f"The package name derived from {host} is too long; pass a shorter one with --package."
        )
    return package_name


def validate_app_label(label: str) -> str:
    label = label.strip()
    if (
        not label
        or len(label) > MAX_APP_LABEL_LENGTH
        or any(not character.isprintable() for character in label)
    ):
        raise BuildError(
            f"--label must be 1 to {MAX_APP_LABEL_LENGTH} printable characters."
        )
    return label


def read_appsettings() -> dict:
    if not APPSETTINGS_PATH.is_file():
        return {}
    try:
        return json.loads(APPSETTINGS_PATH.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as error:
        raise BuildError(f"{APPSETTINGS_PATH} is not valid JSON: {error}") from error


def appsetting(settings: dict, section: str, key: str):
    values = settings.get(section)
    return values.get(key) if isinstance(values, dict) else None


def port_setting(
    argument: int | None, settings: dict, section: str, default: int, option: str
) -> int:
    port = argument or appsetting(settings, section, "Port") or default
    if not isinstance(port, int) or isinstance(port, bool):
        raise BuildError(
            f"{section}.Port in appsettings.json must be a number, got {port!r}."
        )
    return validate_port(port, option)


def resolve_target(arguments: argparse.Namespace) -> HostTarget:
    settings = read_appsettings()
    if "Lobby" in settings:
        raise BuildError("Rename the Lobby settings section to LobbyServer.")
    host = arguments.host or appsetting(settings, "LobbyServer", "Host")
    if not isinstance(host, str) or not host:
        raise BuildError(
            "No server address. Pass --host with your domain or public IPv4 address, or set LobbyServer.Host in appsettings.json."
        )
    host = validate_host(host)
    dir_port = port_setting(
        arguments.dir_port, settings, "DirServer", DEFAULT_DIR_PORT, "--dir-port"
    )
    lobby_port = port_setting(
        None, settings, "LobbyServer", DEFAULT_LOBBY_PORT, "LobbyServer.Port"
    )
    if arguments.dev:
        web_port = port_setting(
            arguments.web_port,
            settings,
            "HttpServer",
            DEFAULT_DEV_WEB_PORT,
            "--web-port",
        )
        if web_port in WEBVIEW_REFUSED_PORTS:
            log(
                f"Warning: Android WebView refuses port {web_port}, so the account login page will not open on this build (guest login still works). Set HttpServer.Port to another port, such as {DEFAULT_DEV_WEB_PORT}."
            )
    else:
        web_port = validate_port(arguments.web_port or DEFAULT_HTTPS_PORT, "--web-port")
    update_port = port_setting(
        None,
        settings,
        "UpdateServer",
        CLIENT_ADDRESS_SERVICE_PORTS[0],
        "UpdateServer.Port",
    )
    if update_port not in CLIENT_ADDRESS_SERVICE_PORTS:
        allowed = " or ".join(str(port) for port in CLIENT_ADDRESS_SERVICE_PORTS)
        raise BuildError(
            f"UpdateServer.Port is {update_port}; the client only looks for the update server on port {allowed}."
        )
    voice_host = arguments.voice_host or appsetting(settings, "VoiceServer", "Host")
    voice_host = (
        validate_host(voice_host)
        if isinstance(voice_host, str) and voice_host
        else host
    )
    voice_port = port_setting(
        arguments.voice_port,
        settings,
        "VoiceServer",
        DEFAULT_VOICE_PORT,
        "--voice-port",
    )
    package_name = validate_package_name(
        arguments.package or default_package_name(host, arguments.dev)
    )
    app_label = validate_app_label(arguments.label or DEFAULT_APP_LABEL)
    target = HostTarget(
        host,
        dir_port,
        lobby_port,
        web_port,
        update_port,
        voice_host,
        voice_port,
        arguments.dev,
        package_name,
        app_label,
    )
    longest_supported = min(
        len(known.dir_address.original)
        for known in CLIENT_BUILDS_BY_METADATA_MD5.values()
    )
    if len(target.dir_address.encode("utf-8")) > longest_supported:
        room = longest_supported - len(DIR_NODE_NAME) - 1
        raise BuildError(
            f"{host}:{target.dir_port} is too long; the client's directory address holds at most {room} characters of host and port."
        )
    shortest_update_host = min(
        len(literal.original)
        for known in CLIENT_BUILDS_BY_METADATA_MD5.values()
        for literal in known.update_hosts
    )
    if len(host.encode("utf-8")) > shortest_update_host:
        raise BuildError(
            f"{host} is too long; the client's update server addresses hold at most {shortest_update_host} characters."
        )
    shortest_image_cdn_address = min(
        len(known.image_cdn_address.original)
        for known in CLIENT_BUILDS_BY_METADATA_MD5.values()
    )
    if len(target.image_cdn_address.encode("utf-8")) > shortest_image_cdn_address:
        raise BuildError(
            f"{target.image_cdn_address} is too long; the client's image CDN address holds at most {shortest_image_cdn_address} characters."
        )
    shortest_gvoice_server_url = min(
        len(known.gvoice_server_url.original)
        for known in CLIENT_BUILDS_BY_METADATA_MD5.values()
    )
    if len(target.gvoice_server_url.encode("utf-8")) > shortest_gvoice_server_url:
        room = shortest_gvoice_server_url - len(GVOICE_SERVER_SCHEME) - 1
        raise BuildError(
            f"{target.gvoice_server_url} is too long; the client's voice server address holds at most {room} characters of host and port. Pass --voice-host with a shorter name or the IPv4 address of the voice server."
        )
    if is_ipv4_literal(host):
        log(
            "Warning: with an IP address the Centauri pay check always fails (it rejects HTTPS to IP hosts) and the client shows a Google Play error; use a domain to avoid it."
        )
    if is_loopback(host) and not arguments.dev:
        log(
            f"Warning: {host} is a loopback address; only a device that reaches this machine through adb reverse can connect."
        )
    return target


def find_split_apks(apk_dir: Path) -> dict[str, Path]:
    if not apk_dir.is_dir():
        raise BuildError(f"APK folder not found: {apk_dir}")
    splits = {name: apk_dir / name for name in SPLIT_APKS}
    missing = [name for name, path in splits.items() if not path.is_file()]
    if missing:
        raise BuildError(
            f"{apk_dir} is missing {', '.join(missing)}. Copy all three official splits ({', '.join(SPLIT_APKS)}) into it."
        )
    return splits


def replace_ini_value(text: str, key: str, value: str) -> str:
    pattern = re.compile(rf"^({re.escape(key)}[ \t]*=[ \t]*)[^\r\n]*", re.MULTILINE)
    replaced, count = pattern.subn(lambda match: match.group(1) + value, text)
    if count != 1:
        raise BuildError(
            f"{MSDK_CONFIG_ENTRY} has {count} {key} lines; expected exactly one."
        )
    return replaced


def patch_msdk_config(content: bytes, target: HostTarget) -> bytes:
    text = content.decode("utf-8")
    values = {
        "MSDK_URL": target.web_origin,
        "POKEMON_LOGIN_URL": f"{target.web_origin}/oauth2/auth",
        "NINTENDO_WEB_LOGIN_URL": f"{target.web_origin}/connect/1.0.0/authorize",
        "POKEMON_REDIRECT_URI": POKEMON_REDIRECT_URI,
        "NINTENDO_WEB_REDIRECT_URL": NINTENDO_REDIRECT_URI,
        **dict.fromkeys(SILENCED_MSDK_HOST_KEYS, UNREACHABLE_ORIGIN),
    }
    for key, value in values.items():
        text = replace_ini_value(text, key, value)
    return text.encode("utf-8")


def decode_pool_string(data: bytes, position: int, utf8: bool) -> str:
    if utf8:
        position += 2 if data[position] & 0x80 else 1
        length = data[position]
        position += 1
        if length & 0x80:
            length = ((length & 0x7F) << 8) | data[position]
            position += 1
        return data[position : position + length].decode("utf-8")
    length = struct.unpack_from("<H", data, position)[0]
    position += 2
    if length & 0x8000:
        length = ((length & 0x7FFF) << 16) | struct.unpack_from("<H", data, position)[0]
        position += 2
    return data[position : position + 2 * length].decode("utf-16-le")


def read_manifest_strings(manifest: bytes) -> tuple[list[str], int]:
    chunk_type, header_size, chunk_size = struct.unpack_from("<HHI", manifest, 8)
    if chunk_type != AXML_STRING_POOL:
        raise BuildError(f"{MANIFEST_ENTRY} has no string pool.")
    count, _, flags, strings_start, _ = struct.unpack_from("<5I", manifest, 16)
    offsets = struct.unpack_from(f"<{count}I", manifest, 8 + header_size)
    strings = [
        decode_pool_string(
            manifest, 8 + strings_start + offset, bool(flags & AXML_UTF8_FLAG)
        )
        for offset in offsets
    ]
    return strings, 8 + chunk_size


def find_application_element(manifest: bytes, strings: list[str], position: int) -> int:
    while position + 8 <= len(manifest):
        chunk_type, _, chunk_size = struct.unpack_from("<HHI", manifest, position)
        if chunk_size < 8:
            raise BuildError(f"{MANIFEST_ENTRY} has a corrupt chunk.")
        name_index = (
            struct.unpack_from("<I", manifest, position + 20)[0]
            if chunk_type == AXML_START_ELEMENT
            else None
        )
        if (
            name_index is not None
            and name_index < len(strings)
            and strings[name_index] == APPLICATION_ELEMENT
        ):
            return position
        position += chunk_size
    raise BuildError(f"{MANIFEST_ENTRY} has no <application> element.")


def application_attributes(
    manifest: bytes, strings: list[str], element: int
) -> tuple[int, int, dict[str, int]]:
    attribute_start, attribute_size, attribute_count = struct.unpack_from(
        "<HHH", manifest, element + 24
    )
    first = element + 16 + attribute_start
    positions = {}
    for index in range(attribute_count):
        name_index = struct.unpack_from(
            "<I", manifest, first + index * attribute_size + 4
        )[0]
        if name_index < len(strings):
            positions[strings[name_index]] = index
    return first, attribute_size, positions


def remove_attribute(
    manifest: bytearray, element: int, removed: int, attribute_size: int, first: int
) -> None:
    start = first + removed * attribute_size
    del manifest[start : start + attribute_size]
    struct.pack_into("<I", manifest, 4, len(manifest))
    struct.pack_into(
        "<I",
        manifest,
        element + 4,
        struct.unpack_from("<I", manifest, element + 4)[0] - attribute_size,
    )
    struct.pack_into(
        "<H",
        manifest,
        element + 28,
        struct.unpack_from("<H", manifest, element + 28)[0] - 1,
    )
    for special_index in (element + 30, element + 32, element + 34):
        one_based = struct.unpack_from("<H", manifest, special_index)[0]
        if one_based == removed + 1:
            struct.pack_into("<H", manifest, special_index, 0)
        elif one_based > removed + 1:
            struct.pack_into("<H", manifest, special_index, one_based - 1)


def drop_required_split_types(manifest: bytes) -> bytes:
    if struct.unpack_from("<HH", manifest, 0) != (0x0003, 8):
        raise BuildError(f"{MANIFEST_ENTRY} is not binary XML.")
    strings, first_chunk = read_manifest_strings(manifest)
    for name, element in manifest_elements(manifest, strings, first_chunk):
        if name != MANIFEST_ELEMENT:
            continue
        first, attribute_size, positions = application_attributes(
            manifest, strings, element
        )
        removed = positions.get(REQUIRED_SPLIT_TYPES_ATTRIBUTE)
        if removed is None:
            raise BuildError(
                f"{MANIFEST_ENTRY} has no android:{REQUIRED_SPLIT_TYPES_ATTRIBUTE} to remove."
            )
        patched = bytearray(manifest)
        remove_attribute(patched, element, removed, attribute_size, first)
        return bytes(patched)
    raise BuildError(f"{MANIFEST_ENTRY} has no <manifest> element.")


def allow_cleartext_traffic(manifest: bytes) -> bytes:
    if struct.unpack_from("<HH", manifest, 0) != (0x0003, 8):
        raise BuildError(f"{MANIFEST_ENTRY} is not binary XML.")
    strings, first_chunk = read_manifest_strings(manifest)
    element = find_application_element(manifest, strings, first_chunk)
    first, attribute_size, positions = application_attributes(
        manifest, strings, element
    )
    if USES_CLEARTEXT_TRAFFIC_ATTRIBUTE not in positions:
        raise BuildError(
            f"{MANIFEST_ENTRY} has no android:{USES_CLEARTEXT_TRAFFIC_ATTRIBUTE} to enable."
        )
    patched = bytearray(manifest)
    cleartext = first + positions[USES_CLEARTEXT_TRAFFIC_ATTRIBUTE] * attribute_size
    patched[cleartext + 15] = AXML_TYPE_BOOLEAN
    struct.pack_into("<I", patched, cleartext + 16, AXML_TRUE)
    removed = positions.get(NETWORK_SECURITY_CONFIG_ATTRIBUTE)
    if removed is not None:
        remove_attribute(patched, element, removed, attribute_size, first)
    return bytes(patched)


def encode_pool_length(length: int, utf8: bool) -> bytes:
    if utf8:
        if length > 0x7FFF:
            raise BuildError(f"{MANIFEST_ENTRY} string is too long to encode.")
        if length > 0x7F:
            return bytes((0x80 | (length >> 8), length & 0xFF))
        return bytes((length,))
    if length > 0x7FFF:
        return struct.pack("<HH", 0x8000 | (length >> 16), length & 0xFFFF)
    return struct.pack("<H", length)


def encode_pool_string(text: str, utf8: bool) -> bytes:
    utf16 = text.encode("utf-16-le")
    if not utf8:
        return encode_pool_length(len(utf16) // 2, False) + utf16 + b"\x00\x00"
    encoded = text.encode("utf-8")
    return (
        encode_pool_length(len(utf16) // 2, True)
        + encode_pool_length(len(encoded), True)
        + encoded
        + b"\x00"
    )


def encode_string_pool(strings: list[str], flags: int) -> bytes:
    utf8 = bool(flags & AXML_UTF8_FLAG)
    offsets = []
    data = bytearray()
    for text in strings:
        offsets.append(len(data))
        data += encode_pool_string(text, utf8)
    data += b"\x00" * (-len(data) % 4)
    strings_start = AXML_STRING_POOL_HEADER_SIZE + 4 * len(strings)
    header = struct.pack(
        "<HHI5I",
        AXML_STRING_POOL,
        AXML_STRING_POOL_HEADER_SIZE,
        strings_start + len(data),
        len(strings),
        0,
        flags,
        strings_start,
        0,
    )
    return header + struct.pack(f"<{len(strings)}I", *offsets) + bytes(data)


def manifest_elements(manifest: bytes, strings: list[str], position: int):
    while position + 8 <= len(manifest):
        chunk_type, _, chunk_size = struct.unpack_from("<HHI", manifest, position)
        if chunk_size < 8:
            raise BuildError(f"{MANIFEST_ENTRY} has a corrupt chunk.")
        if chunk_type == AXML_START_ELEMENT:
            name_index = struct.unpack_from("<I", manifest, position + 20)[0]
            if name_index < len(strings):
                yield strings[name_index], position
        position += chunk_size


def manifest_package_name(manifest: bytes) -> str:
    strings, first_chunk = read_manifest_strings(manifest)
    for name, element in manifest_elements(manifest, strings, first_chunk):
        if name != MANIFEST_ELEMENT:
            continue
        first, attribute_size, positions = application_attributes(
            manifest, strings, element
        )
        if PACKAGE_ATTRIBUTE in positions:
            attribute = first + positions[PACKAGE_ATTRIBUTE] * attribute_size
            return strings[struct.unpack_from("<I", manifest, attribute + 8)[0]]
    raise BuildError(f"{MANIFEST_ENTRY} has no package name.")


def set_literal_labels(
    manifest: bytearray, strings: list[str], label_index: int
) -> int:
    _, first_chunk = read_manifest_strings(bytes(manifest))
    labelled = 0
    for name, element in manifest_elements(bytes(manifest), strings, first_chunk):
        if name not in LABELLED_ELEMENTS:
            continue
        first, attribute_size, positions = application_attributes(
            bytes(manifest), strings, element
        )
        if LABEL_ATTRIBUTE not in positions:
            continue
        attribute = first + positions[LABEL_ATTRIBUTE] * attribute_size
        struct.pack_into("<I", manifest, attribute + 8, label_index)
        manifest[attribute + 15] = AXML_TYPE_STRING
        struct.pack_into("<I", manifest, attribute + 16, label_index)
        labelled += 1
    return labelled


def rename_manifest_package(
    manifest: bytes, official_package: str, package_name: str, label: str | None
) -> bytes:
    if struct.unpack_from("<HH", manifest, 0) != (0x0003, 8):
        raise BuildError(f"{MANIFEST_ENTRY} is not binary XML.")
    strings, pool_end = read_manifest_strings(manifest)
    style_count, flags = struct.unpack_from("<4xII", manifest, 16)
    if style_count:
        raise BuildError(
            f"{MANIFEST_ENTRY} has styled strings, which are not supported."
        )
    if manifest_package_name(manifest) != official_package:
        raise BuildError(
            f"{MANIFEST_ENTRY} names package {manifest_package_name(manifest)}, not {official_package}."
        )
    renamed = [text.replace(official_package, package_name) for text in strings]
    if label is not None:
        renamed.append(label)
    pool = encode_string_pool(renamed, flags)
    patched = bytearray(manifest[:8] + pool + manifest[pool_end:])
    struct.pack_into("<I", patched, 4, len(patched))
    if label is not None and not set_literal_labels(patched, renamed, len(renamed) - 1):
        raise BuildError(f"{MANIFEST_ENTRY} has no android:label to replace.")
    return bytes(patched)


def rename_resource_table_package(
    table: bytes, official_package: str, package_name: str
) -> bytes:
    chunk_type, header_size, chunk_size = struct.unpack_from("<HHI", table, 0)
    if chunk_type != RESOURCE_TABLE_TYPE or chunk_size != len(table):
        raise BuildError(f"{RESOURCE_TABLE_ENTRY} is not a resource table.")
    patched = bytearray(table)
    name_bytes = RESOURCE_TABLE_PACKAGE_NAME_UNITS * 2
    renamed = 0
    position = header_size
    while position + 8 <= len(table):
        chunk_type, _, chunk_size = struct.unpack_from("<HHI", table, position)
        if chunk_size < 8:
            raise BuildError(f"{RESOURCE_TABLE_ENTRY} has a corrupt chunk.")
        if chunk_type == RESOURCE_TABLE_PACKAGE_TYPE:
            name_start = position + 12
            name = (
                table[name_start : name_start + name_bytes]
                .decode("utf-16-le")
                .split("\x00", 1)[0]
            )
            if name == official_package:
                patched[name_start : name_start + name_bytes] = package_name.encode(
                    "utf-16-le"
                ).ljust(name_bytes, b"\x00")
                renamed += 1
        position += chunk_size
    if renamed != 1:
        raise BuildError(
            f"{RESOURCE_TABLE_ENTRY} has {renamed} packages named {official_package}, expected one."
        )
    return bytes(patched)


def centauri_ip_list(host: str) -> list[str]:
    if is_ipv4_literal(host):
        return [host]
    try:
        addresses = sorted(
            {info[4][0] for info in socket.getaddrinfo(host, None, socket.AF_INET)}
        )
    except socket.gaierror:
        log(
            f"Warning: {host} does not resolve; the Centauri pay config gets an empty IP list."
        )
        return []
    return addresses


def patch_centauri_config(content: bytes, target: HostTarget) -> bytes:
    try:
        config = json.loads(content.decode("utf-8"))
        entries = config["config"]
    except (UnicodeDecodeError, json.JSONDecodeError, KeyError, TypeError) as error:
        raise BuildError(
            f"{CENTAURI_CONFIG_ENTRY} is not in the expected format: {error}"
        ) from error
    release_entries = [
        entry
        for entry in entries
        if isinstance(entry, dict) and entry.get("mode") == CENTAURI_RELEASE_MODE
    ]
    if len(release_entries) != 1:
        raise BuildError(
            f"{CENTAURI_CONFIG_ENTRY} has {len(release_entries)} release entries; expected exactly one."
        )
    release = release_entries[0]
    release["domain"] = target.host
    release["reportdomain"] = target.host
    release["iplist"] = centauri_ip_list(target.host)
    return (json.dumps(config, indent=4) + "\n").encode("utf-8")


def patch_res_data(
    content: bytes, old_metadata_md5: str, new_metadata_md5: str
) -> bytes:
    text = content.decode("utf-8")
    entries = json.loads(text)
    metadata_entries = [
        entry
        for files in entries.values()
        for entry in files
        if entry.get("path") == f"/{METADATA_ARCHIVE_PATH}"
    ]
    if not metadata_entries or any(
        entry.get("md5") != old_metadata_md5 for entry in metadata_entries
    ):
        raise BuildError(
            f"{RES_DATA_ENTRY} does not list the expected global-metadata.dat md5 {old_metadata_md5}."
        )
    return text.replace(
        f'"md5": "{old_metadata_md5}"', f'"md5": "{new_metadata_md5}"'
    ).encode("utf-8")


def read_metadata_literal(
    metadata: bytearray, build: ClientBuild, literal: MetadataLiteral
) -> tuple[int, int]:
    length, relative_offset = struct.unpack_from("<II", metadata, literal.entry_offset)
    data_offset = build.string_literal_data_offset + relative_offset
    current = bytes(metadata[data_offset : data_offset + length])
    if current != literal.original.encode("utf-8"):
        raise BuildError(
            f"global-metadata.dat does not hold {literal.original!r} where build {build.version_code} keeps it."
        )
    return length, data_offset


def write_metadata_literal(
    metadata: bytearray, build: ClientBuild, literal: MetadataLiteral, value: str
) -> None:
    length, data_offset = read_metadata_literal(metadata, build, literal)
    encoded = value.encode("utf-8")
    if len(encoded) > length:
        raise BuildError(
            f"{value!r} is {len(encoded)} bytes; the client slot for {literal.original!r} holds at most {length}. Use a shorter domain."
        )
    metadata[data_offset : data_offset + length] = encoded.ljust(length, b"\x00")
    struct.pack_into("<I", metadata, literal.entry_offset, len(encoded))


def client_build_for(metadata: bytes) -> ClientBuild:
    metadata_md5 = hashlib.md5(metadata).hexdigest()
    build = CLIENT_BUILDS_BY_METADATA_MD5.get(metadata_md5)
    if build is None:
        supported = ", ".join(
            str(known.version_code) for known in CLIENT_BUILDS_BY_METADATA_MD5.values()
        )
        raise BuildError(
            f"Unsupported client build (global-metadata.dat md5 {metadata_md5}). Supported versionCodes: {supported}."
        )
    return build


def patch_metadata(metadata: bytes, target: HostTarget) -> bytes:
    build = client_build_for(metadata)
    patched = bytearray(metadata)
    write_metadata_literal(patched, build, build.dir_address, target.dir_address)
    write_metadata_literal(
        patched, build, build.cloud_control_host, CLOUD_CONTROL_REPLACEMENT_HOST
    )
    write_metadata_literal(
        patched, build, build.image_cdn_address, target.image_cdn_address
    )
    write_metadata_literal(
        patched, build, build.gvoice_server_url, target.gvoice_server_url
    )
    for update_host in build.update_hosts:
        write_metadata_literal(patched, build, update_host, target.host)
    return bytes(patched)


class SevenZipReader:
    def __init__(self, data: bytes):
        self.data = data
        self.position = 0

    def byte(self) -> int:
        if self.position >= len(self.data):
            raise BuildError("The 7z header ended early.")
        value = self.data[self.position]
        self.position += 1
        return value

    def take(self, count: int) -> bytes:
        if self.position + count > len(self.data):
            raise BuildError("The 7z header ended early.")
        chunk = self.data[self.position : self.position + count]
        self.position += count
        return chunk

    def number(self) -> int:
        first = self.byte()
        mask = 0x80
        value = 0
        for extra_bytes in range(8):
            if first & mask == 0:
                return value | ((first & (mask - 1)) << (8 * extra_bytes))
            value |= self.byte() << (8 * extra_bytes)
            mask >>= 1
        return value

    def expect(self, property_id: int) -> None:
        found = self.byte()
        if found != property_id:
            raise BuildError(
                f"Unexpected 7z property 0x{found:02X}, expected 0x{property_id:02X}."
            )

    def bit_vector(self, count: int) -> list[bool]:
        bits = []
        current = 0
        for index in range(count):
            if index % 8 == 0:
                current = self.byte()
            bits.append(bool(current & (0x80 >> (index % 8))))
        return bits

    def digests(self, count: int) -> list[int | None]:
        defined = [True] * count if self.byte() else self.bit_vector(count)
        return [
            struct.unpack("<I", self.take(4))[0] if is_defined else None
            for is_defined in defined
        ]


def encode_seven_zip_number(value: int) -> bytes:
    for extra_bytes in range(8):
        if value < 1 << (7 * extra_bytes + 7):
            leading_ones = (0xFF << (8 - extra_bytes)) & 0xFF
            return bytes([leading_ones | (value >> (8 * extra_bytes))]) + (
                value & ((1 << (8 * extra_bytes)) - 1)
            ).to_bytes(extra_bytes, "little")
    return b"\xff" + value.to_bytes(8, "little")


def encode_bit_vector(bits: list[bool]) -> bytes:
    encoded = bytearray((len(bits) + 7) // 8)
    for index, bit in enumerate(bits):
        if bit:
            encoded[index // 8] |= 0x80 >> (index % 8)
    return bytes(encoded)


def encode_digests(digests: list[int | None]) -> bytes:
    defined = [digest is not None for digest in digests]
    encoded = b"\x01" if all(defined) else b"\x00" + encode_bit_vector(defined)
    return encoded + b"".join(
        struct.pack("<I", digest) for digest in digests if digest is not None
    )


@dataclass
class SevenZipFolder:
    coder_id: bytes
    coder_properties: bytes
    unpack_size: int
    crc: int | None
    substream_sizes: list[int] = field(default_factory=list)
    substream_crcs: list[int | None] = field(default_factory=list)


@dataclass
class SevenZipArchive:
    pack_position: int
    pack_sizes: list[int]
    folders: list[SevenZipFolder]
    files_info: bytes
    stream_names: list[str]


def read_folder(reader: SevenZipReader) -> SevenZipFolder:
    if reader.number() != 1:
        raise BuildError("Only single-coder 7z folders are supported.")
    flags = reader.byte()
    if flags & 0xD0:
        raise BuildError("Only simple 7z coders are supported.")
    coder_id = reader.take(flags & 0x0F)
    coder_properties = reader.take(reader.number()) if flags & 0x20 else b""
    return SevenZipFolder(coder_id, coder_properties, 0, None)


def read_pack_info(reader: SevenZipReader) -> tuple[int, list[int]]:
    pack_position = reader.number()
    pack_sizes = [0] * reader.number()
    while (property_id := reader.byte()) != SevenZipId.END:
        if property_id == SevenZipId.SIZE:
            pack_sizes = [reader.number() for _ in pack_sizes]
        elif property_id == SevenZipId.CRC:
            reader.digests(len(pack_sizes))
        else:
            raise BuildError(f"Unsupported 7z pack property 0x{property_id:02X}.")
    return pack_position, pack_sizes


def read_unpack_info(reader: SevenZipReader) -> list[SevenZipFolder]:
    reader.expect(SevenZipId.FOLDER)
    folder_count = reader.number()
    if reader.byte() != 0:
        raise BuildError("External 7z folder lists are not supported.")
    folders = [read_folder(reader) for _ in range(folder_count)]
    reader.expect(SevenZipId.CODERS_UNPACK_SIZE)
    for folder in folders:
        folder.unpack_size = reader.number()
    while (property_id := reader.byte()) != SevenZipId.END:
        if property_id != SevenZipId.CRC:
            raise BuildError(f"Unsupported 7z unpack property 0x{property_id:02X}.")
        for folder, crc in zip(folders, reader.digests(folder_count)):
            folder.crc = crc
    return folders


def read_substreams_info(reader: SevenZipReader, folders: list[SevenZipFolder]) -> None:
    stream_counts = [1] * len(folders)
    property_id = reader.byte()
    if property_id == SevenZipId.NUM_UNPACK_STREAM:
        stream_counts = [reader.number() for _ in folders]
        property_id = reader.byte()
    sizes_given = property_id == SevenZipId.SIZE
    for folder, stream_count in zip(folders, stream_counts):
        if stream_count == 0:
            continue
        leading = (
            [reader.number() for _ in range(stream_count - 1)] if sizes_given else []
        )
        folder.substream_sizes = [*leading, folder.unpack_size - sum(leading)]
        folder.substream_crcs = [
            folder.crc if stream_count == 1 else None
        ] * stream_count
    if sizes_given:
        property_id = reader.byte()
    while property_id != SevenZipId.END:
        if property_id != SevenZipId.CRC:
            raise BuildError(f"Unsupported 7z substream property 0x{property_id:02X}.")
        unknown = [
            (folder, index)
            for folder in folders
            for index, crc in enumerate(folder.substream_crcs)
            if not (len(folder.substream_crcs) == 1 and folder.crc is not None)
        ]
        for (folder, index), crc in zip(unknown, reader.digests(len(unknown))):
            folder.substream_crcs[index] = crc
        property_id = reader.byte()


def read_streams_info(
    reader: SevenZipReader,
) -> tuple[int, list[int], list[SevenZipFolder]]:
    pack_position, pack_sizes, folders = 0, [], []
    while (property_id := reader.byte()) != SevenZipId.END:
        if property_id == SevenZipId.PACK_INFO:
            pack_position, pack_sizes = read_pack_info(reader)
        elif property_id == SevenZipId.UNPACK_INFO:
            folders = read_unpack_info(reader)
        elif property_id == SevenZipId.SUBSTREAMS_INFO:
            read_substreams_info(reader, folders)
        else:
            raise BuildError(f"Unsupported 7z streams property 0x{property_id:02X}.")
    for folder in folders:
        if not folder.substream_sizes:
            folder.substream_sizes = [folder.unpack_size]
            folder.substream_crcs = [folder.crc]
    return pack_position, pack_sizes, folders


def read_stream_names(files_info: bytes) -> list[str]:
    reader = SevenZipReader(files_info)
    reader.expect(SevenZipId.FILES_INFO)
    file_count = reader.number()
    names: list[str] = []
    empty_streams = [False] * file_count
    while (property_id := reader.byte()) != SevenZipId.END:
        payload = SevenZipReader(reader.take(reader.number()))
        if property_id == SevenZipId.NAME:
            if payload.byte() != 0:
                raise BuildError("External 7z file names are not supported.")
            names = (
                payload.data[payload.position :]
                .decode("utf-16-le")
                .split("\x00")[:file_count]
            )
        elif property_id == SevenZipId.EMPTY_STREAM:
            empty_streams = payload.bit_vector(file_count)
    if len(names) != file_count:
        raise BuildError("The 7z archive has no complete file name list.")
    return [name for name, is_empty in zip(names, empty_streams) if not is_empty]


def files_info_end(header: bytes, start: int) -> int:
    reader = SevenZipReader(header)
    reader.position = start + 1
    reader.number()
    while reader.byte() != SevenZipId.END:
        reader.take(reader.number())
    return reader.position


def lzma_filters(folder: SevenZipFolder) -> list[dict]:
    if folder.coder_id == SEVEN_ZIP_LZMA2_CODER:
        dictionary_property = folder.coder_properties[0]
        dictionary_size = (2 | (dictionary_property & 1)) << (
            dictionary_property // 2 + 11
        )
        return [{"id": lzma.FILTER_LZMA2, "dict_size": dictionary_size}]
    if folder.coder_id == SEVEN_ZIP_LZMA_CODER:
        properties, dictionary_size = (
            folder.coder_properties[0],
            struct.unpack("<I", folder.coder_properties[1:5])[0],
        )
        return [
            {
                "id": lzma.FILTER_LZMA1,
                "lc": properties % 9,
                "lp": properties // 9 % 5,
                "pb": properties // 45,
                "dict_size": dictionary_size,
            }
        ]
    raise BuildError(f"Unsupported 7z coder {folder.coder_id.hex()}.")


def unpack_folder(folder: SevenZipFolder, packed: bytes) -> bytes:
    unpacked = lzma.LZMADecompressor(
        lzma.FORMAT_RAW, filters=lzma_filters(folder)
    ).decompress(packed, folder.unpack_size)
    if len(unpacked) != folder.unpack_size:
        raise BuildError("A 7z folder unpacked to the wrong size.")
    return unpacked


def read_header_bytes(archive: bytes) -> bytes:
    if archive[:6] != SEVEN_ZIP_SIGNATURE:
        raise BuildError(f"{RESOURCES_ARCHIVE_ENTRY} is not a 7z archive.")
    next_header_offset, next_header_size, next_header_crc = struct.unpack_from(
        "<QQI", archive, 12
    )
    start = SEVEN_ZIP_START_HEADER_SIZE + next_header_offset
    header = archive[start : start + next_header_size]
    if zlib.crc32(header) != next_header_crc:
        raise BuildError(f"{RESOURCES_ARCHIVE_ENTRY} has a corrupt 7z header.")
    if header[0] == SevenZipId.ENCODED_HEADER:
        reader = SevenZipReader(header)
        reader.position = 1
        pack_position, pack_sizes, folders = read_streams_info(reader)
        offset = SEVEN_ZIP_START_HEADER_SIZE + pack_position
        header = unpack_folder(folders[0], archive[offset : offset + pack_sizes[0]])
    return header


def read_seven_zip(archive: bytes) -> SevenZipArchive:
    header = read_header_bytes(archive)
    reader = SevenZipReader(header)
    reader.expect(SevenZipId.HEADER)
    property_id = reader.byte()
    if property_id in (
        SevenZipId.ARCHIVE_PROPERTIES,
        SevenZipId.ADDITIONAL_STREAMS_INFO,
    ):
        raise BuildError("7z archive properties are not supported.")
    if property_id != SevenZipId.MAIN_STREAMS_INFO:
        raise BuildError("The 7z archive has no streams.")
    pack_position, pack_sizes, folders = read_streams_info(reader)
    if len(pack_sizes) != len(folders):
        raise BuildError("The 7z archive must have one pack stream per folder.")
    files_start = reader.position
    if header[files_start] != SevenZipId.FILES_INFO:
        raise BuildError("The 7z archive has no file list.")
    files_info = header[files_start : files_info_end(header, files_start)]
    stream_names = read_stream_names(files_info)
    if len(stream_names) != sum(len(folder.substream_sizes) for folder in folders):
        raise BuildError("The 7z file list does not match its streams.")
    return SevenZipArchive(pack_position, pack_sizes, folders, files_info, stream_names)


def encode_streams_info(archive: SevenZipArchive) -> bytes:
    encoded = bytearray([SevenZipId.MAIN_STREAMS_INFO, SevenZipId.PACK_INFO])
    encoded += encode_seven_zip_number(archive.pack_position) + encode_seven_zip_number(
        len(archive.pack_sizes)
    )
    encoded += bytes([SevenZipId.SIZE]) + b"".join(
        encode_seven_zip_number(size) for size in archive.pack_sizes
    )
    encoded += (
        bytes([SevenZipId.END, SevenZipId.UNPACK_INFO, SevenZipId.FOLDER])
        + encode_seven_zip_number(len(archive.folders))
        + b"\x00"
    )
    for folder in archive.folders:
        flags = len(folder.coder_id) | (0x20 if folder.coder_properties else 0)
        encoded += b"\x01" + bytes([flags]) + folder.coder_id
        if folder.coder_properties:
            encoded += (
                encode_seven_zip_number(len(folder.coder_properties))
                + folder.coder_properties
            )
    encoded += bytes([SevenZipId.CODERS_UNPACK_SIZE]) + b"".join(
        encode_seven_zip_number(folder.unpack_size) for folder in archive.folders
    )
    if any(folder.crc is not None for folder in archive.folders):
        encoded += bytes([SevenZipId.CRC]) + encode_digests(
            [folder.crc for folder in archive.folders]
        )
    encoded += bytes(
        [SevenZipId.END, SevenZipId.SUBSTREAMS_INFO, SevenZipId.NUM_UNPACK_STREAM]
    )
    encoded += b"".join(
        encode_seven_zip_number(len(folder.substream_sizes))
        for folder in archive.folders
    )
    encoded += bytes([SevenZipId.SIZE]) + b"".join(
        encode_seven_zip_number(size)
        for folder in archive.folders
        for size in folder.substream_sizes[:-1]
    )
    unknown_crcs = [
        crc
        for folder in archive.folders
        if not (len(folder.substream_sizes) == 1 and folder.crc is not None)
        for crc in folder.substream_crcs
    ]
    if unknown_crcs:
        encoded += bytes([SevenZipId.CRC]) + encode_digests(unknown_crcs)
    encoded += bytes([SevenZipId.END, SevenZipId.END])
    return bytes(encoded)


def write_seven_zip(archive: SevenZipArchive, packed_streams: list[bytes]) -> bytes:
    archive.pack_position = 0
    archive.pack_sizes = [len(stream) for stream in packed_streams]
    header = (
        bytes([SevenZipId.HEADER])
        + encode_streams_info(archive)
        + archive.files_info
        + bytes([SevenZipId.END])
    )
    body = b"".join(packed_streams)
    next_header = struct.pack("<QQI", len(body), len(header), zlib.crc32(header))
    start_header = (
        SEVEN_ZIP_SIGNATURE
        + SEVEN_ZIP_VERSION
        + struct.pack("<I", zlib.crc32(next_header))
        + next_header
    )
    return start_header + body + header


def replace_seven_zip_file(
    archive_bytes: bytes, file_name: str, transform: Callable[[bytes], bytes]
) -> tuple[bytes, bytes, bytes]:
    archive = read_seven_zip(archive_bytes)
    if file_name not in archive.stream_names:
        raise BuildError(f"{RESOURCES_ARCHIVE_ENTRY} has no {file_name}.")
    target_stream = archive.stream_names.index(file_name)
    packed_streams = []
    pack_offset = SEVEN_ZIP_START_HEADER_SIZE + archive.pack_position
    first_stream = 0
    original = patched = b""
    for folder, pack_size in zip(archive.folders, archive.pack_sizes):
        packed = archive_bytes[pack_offset : pack_offset + pack_size]
        pack_offset += pack_size
        stream_range = range(first_stream, first_stream + len(folder.substream_sizes))
        first_stream = stream_range.stop
        if target_stream not in stream_range:
            packed_streams.append(packed)
            continue
        unpacked = unpack_folder(folder, packed)
        index = target_stream - stream_range.start
        start = sum(folder.substream_sizes[:index])
        original = unpacked[start : start + folder.substream_sizes[index]]
        if folder.substream_crcs[index] not in (None, zlib.crc32(original)):
            raise BuildError(
                f"{file_name} in {RESOURCES_ARCHIVE_ENTRY} fails its CRC check."
            )
        patched = transform(original)
        if len(patched) != len(original):
            raise BuildError(f"Patching {file_name} changed its size.")
        unpacked = unpacked[:start] + patched + unpacked[start + len(patched) :]
        folder.substream_crcs[index] = zlib.crc32(patched)
        if folder.crc is not None:
            folder.crc = zlib.crc32(unpacked)
        log(
            f"  Recompressing {len(unpacked) // (1024 * 1024)} MB of {RESOURCES_ARCHIVE_ENTRY}..."
        )
        filters = [{**lzma_filters(folder)[0], "preset": LZMA2_PRESET}]
        packed_streams.append(
            lzma.compress(unpacked, format=lzma.FORMAT_RAW, filters=filters)
        )
    return write_seven_zip(archive, packed_streams), original, patched


def alignment_for(name: str, method: int) -> int:
    if method != ZIP_STORED:
        return 1
    return NATIVE_LIBRARY_ALIGNMENT if name.endswith(".so") else STORED_ENTRY_ALIGNMENT


def alignment_extra(data_offset_without_extra: int, alignment: int) -> bytes:
    if alignment == 1:
        return b""
    padding = (
        -(data_offset_without_extra + ANDROID_ALIGNMENT_EXTRA_MIN_SIZE)
    ) % alignment
    return (
        struct.pack("<HHH", ANDROID_ALIGNMENT_EXTRA_ID, 2 + padding, alignment)
        + b"\x00" * padding
    )


def deflate(data: bytes) -> bytes:
    compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
    return compressor.compress(data) + compressor.flush()


def copy_range(source, destination, start: int, length: int) -> None:
    source.seek(start)
    while length > 0:
        chunk = source.read(min(COPY_CHUNK_SIZE, length))
        if not chunk:
            raise BuildError("An APK entry ended early.")
        destination.write(chunk)
        length -= len(chunk)


@dataclass
class MergedApkPart:
    path: Path
    replacements: dict[str, bytes]
    excluded: frozenset[str] = frozenset()


def merge_apks(parts: list[MergedApkPart], destination_path: Path) -> None:
    central_directory = bytearray()
    written = 0
    kept_entries = 0
    merged_names = set()
    with destination_path.open("wb") as destination:
        for part in parts:
            written, kept_entries = copy_apk_entries(
                part,
                destination,
                central_directory,
                merged_names,
                written,
                kept_entries,
            )
        if kept_entries >= 0xFFFF or written >= 0xFFFFFFFF:
            raise BuildError(
                f"{destination_path.name} would need ZIP64, which is not supported."
            )
        destination.write(central_directory)
        destination.write(
            ZIP_END_OF_CENTRAL_DIRECTORY.pack(
                ZIP_END_SIGNATURE,
                0,
                0,
                kept_entries,
                kept_entries,
                len(central_directory),
                written,
                0,
            )
        )


def copy_apk_entries(
    part: MergedApkPart,
    destination,
    central_directory: bytearray,
    merged_names: set[str],
    written: int,
    kept_entries: int,
) -> tuple[int, int]:
    source_path, replacements = part.path, part.replacements
    with zipfile.ZipFile(source_path) as source_zip:
        entries = source_zip.infolist()
    names = {entry.filename for entry in entries}
    missing = sorted(set(replacements) - names)
    if missing:
        raise BuildError(f"{source_path.name} has no {', '.join(missing)}.")
    with source_path.open("rb") as source:
        for entry in entries:
            if (
                V1_SIGNATURE_FILE.match(entry.filename)
                or entry.filename in part.excluded
            ):
                continue
            if entry.filename in merged_names:
                raise BuildError(
                    f"{source_path.name} repeats {entry.filename} from an earlier split."
                )
            merged_names.add(entry.filename)
            if (
                entry.file_size >= 0xFFFFFFFF
                or entry.compress_size >= 0xFFFFFFFF
                or entry.header_offset >= 0xFFFFFFFF
            ):
                raise BuildError(
                    f"{source_path.name} uses ZIP64, which is not supported."
                )
            source.seek(entry.header_offset)
            local = ZIP_LOCAL_HEADER.unpack(source.read(ZIP_LOCAL_HEADER.size))
            if local[0] != ZIP_LOCAL_SIGNATURE:
                raise BuildError(
                    f"{source_path.name} has a corrupt entry {entry.filename}."
                )
            raw_name = source.read(local[9])
            data_start = (
                entry.header_offset + ZIP_LOCAL_HEADER.size + local[9] + local[10]
            )
            mod_time, mod_date = local[4], local[5]
            flags = entry.flag_bits & ~ZIP_DATA_DESCRIPTOR_FLAG
            method, crc, compressed_size, file_size = (
                entry.compress_type,
                entry.CRC,
                entry.compress_size,
                entry.file_size,
            )
            replacement = replacements.get(entry.filename)
            if replacement is not None:
                compressed = (
                    replacement if method == ZIP_STORED else deflate(replacement)
                )
                method = ZIP_STORED if method == ZIP_STORED else ZIP_DEFLATED
                crc, compressed_size, file_size = (
                    zlib.crc32(replacement),
                    len(compressed),
                    len(replacement),
                )
            extra = alignment_extra(
                written + ZIP_LOCAL_HEADER.size + len(raw_name),
                alignment_for(entry.filename, method),
            )
            local_offset = written
            destination.write(
                ZIP_LOCAL_HEADER.pack(
                    ZIP_LOCAL_SIGNATURE,
                    entry.extract_version,
                    flags,
                    method,
                    mod_time,
                    mod_date,
                    crc,
                    compressed_size,
                    file_size,
                    len(raw_name),
                    len(extra),
                )
            )
            destination.write(raw_name + extra)
            if replacement is not None:
                destination.write(compressed)
            else:
                copy_range(source, destination, data_start, compressed_size)
            written += (
                ZIP_LOCAL_HEADER.size + len(raw_name) + len(extra) + compressed_size
            )
            central_directory += (
                ZIP_CENTRAL_HEADER.pack(
                    ZIP_CENTRAL_SIGNATURE,
                    entry.create_version | (entry.create_system << 8),
                    entry.extract_version,
                    flags,
                    method,
                    mod_time,
                    mod_date,
                    crc,
                    compressed_size,
                    file_size,
                    len(raw_name),
                    0,
                    0,
                    0,
                    entry.internal_attr,
                    entry.external_attr,
                    local_offset,
                )
                + raw_name
            )
            kept_entries += 1
    return written, kept_entries


def find_java() -> Path:
    java_home = os.environ.get("JAVA_HOME")
    executable = "java.exe" if os.name == "nt" else "java"
    candidates = [Path(java_home) / "bin" / executable] if java_home else []
    on_path = shutil.which("java")
    if on_path:
        candidates.append(Path(on_path))
    for candidate in candidates:
        if candidate.is_file():
            return candidate
    raise BuildError(
        "Java was not found. Install a JDK (17 or newer, for example Eclipse Temurin) and put it on PATH or set JAVA_HOME."
    )


def find_keytool(java: Path) -> Path:
    keytool = java.with_name("keytool.exe" if os.name == "nt" else "keytool")
    if keytool.is_file():
        return keytool
    on_path = shutil.which("keytool")
    if on_path:
        return Path(on_path)
    raise BuildError(
        "keytool was not found next to Java. Install a full JDK rather than a JRE."
    )


def version_key(path: Path) -> tuple:
    return tuple(
        int(part) if part.isdigit() else -1 for part in re.split(r"[.\-]", path.name)
    )


def find_installed_apksigner(work_dir: Path) -> Path | None:
    roots = [
        Path(os.environ[name]) / "build-tools"
        for name in ("ANDROID_HOME", "ANDROID_SDK_ROOT")
        if os.environ.get(name)
    ]
    roots.append(work_dir / "android-build-tools")
    for root in roots:
        if not root.is_dir():
            continue
        for version_dir in sorted(root.iterdir(), key=version_key, reverse=True):
            jars = sorted(version_dir.glob("**/lib/apksigner.jar"))
            if jars:
                return jars[0]
    return None


def local_name(element: ElementTree.Element) -> str:
    return element.tag.rsplit("}", 1)[-1]


def child(element: ElementTree.Element, name: str) -> ElementTree.Element | None:
    return next((item for item in element if local_name(item) == name), None)


def latest_build_tools_archive() -> tuple[str, str, str]:
    host_os = {
        "nt": "windows",
        "posix": "macosx" if sys.platform == "darwin" else "linux",
    }[os.name]
    with urllib.request.urlopen(ANDROID_REPOSITORY_INDEX, timeout=60) as response:
        root = ElementTree.fromstring(response.read())
    best = None
    for package in root.iter():
        if local_name(package) != "remotePackage" or not package.get(
            "path", ""
        ).startswith("build-tools;"):
            continue
        channel = child(package, "channelRef")
        revision = child(package, "revision")
        if (
            channel is None
            or channel.get("ref") != "channel-0"
            or revision is None
            or child(revision, "preview") is not None
        ):
            continue
        version = tuple(
            int(child(revision, part).text) if child(revision, part) is not None else 0
            for part in ("major", "minor", "micro")
        )
        archives = child(package, "archives")
        for archive in archives if archives is not None else []:
            archive_os = child(archive, "host-os")
            complete = child(archive, "complete")
            if complete is None or (
                archive_os is not None and archive_os.text != host_os
            ):
                continue
            checksum = child(complete, "checksum")
            url = child(complete, "url")
            if (
                checksum is not None
                and url is not None
                and (best is None or version > best[0])
            ):
                best = (version, url.text, checksum.text)
    if best is None:
        raise BuildError(
            "Could not find Android SDK Build-Tools for this system in Google's repository."
        )
    return ".".join(map(str, best[0])), ANDROID_REPOSITORY_URL + best[1], best[2]


def download_build_tools(work_dir: Path, assume_yes: bool) -> Path:
    version, url, sha1 = latest_build_tools_archive()
    log(
        "apksigner is needed to sign the APKs and was not found (set ANDROID_HOME to use an installed Android SDK)."
    )
    log(
        f"build_apk can download Android SDK Build-Tools {version} from Google ({url})."
    )
    log(
        f"It is covered by the Android Software Development Kit License Agreement: {ANDROID_SDK_LICENSE_URL}"
    )
    if not assume_yes:
        if not sys.stdin.isatty():
            raise BuildError(
                "Run again with --yes to accept the license and download the build tools, or set ANDROID_HOME."
            )
        try:
            answer = input("Accept the license and download? [y/N] ")
        except EOFError:
            answer = ""
        if answer.strip().lower() not in ("y", "yes"):
            raise BuildError("Build tools are required to sign the APKs.")
    destination = work_dir / "android-build-tools" / version
    with tempfile.TemporaryDirectory(dir=work_dir) as temporary:
        archive_path = Path(temporary) / "build-tools.zip"
        log(f"Downloading build tools {version}...")
        digest = hashlib.sha1()
        with (
            urllib.request.urlopen(url, timeout=600) as response,
            archive_path.open("wb") as archive,
        ):
            while chunk := response.read(COPY_CHUNK_SIZE):
                digest.update(chunk)
                archive.write(chunk)
        if digest.hexdigest() != sha1:
            raise BuildError("The downloaded build tools failed their checksum.")
        with zipfile.ZipFile(archive_path) as archive:
            for member in archive.namelist():
                if Path(member).is_absolute() or ".." in Path(member).parts:
                    raise BuildError(
                        "The downloaded build tools archive has an unsafe path."
                    )
            archive.extractall(destination)
    jars = sorted(destination.glob("**/lib/apksigner.jar"))
    if not jars:
        raise BuildError("The downloaded build tools have no apksigner.jar.")
    return jars[0]


def find_signing_tools(work_dir: Path, assume_yes: bool) -> SigningTools:
    java = find_java()
    keytool = find_keytool(java)
    apksigner = find_installed_apksigner(work_dir) or download_build_tools(
        work_dir, assume_yes
    )
    return SigningTools(java, keytool, apksigner)


def run_tool(command: list[str | Path], action: str) -> None:
    result = subprocess.run(
        [str(part) for part in command],
        check=False,
        capture_output=True,
        text=True,
        errors="replace",
    )
    if result.returncode != 0:
        details = (result.stderr or result.stdout).strip()
        raise BuildError(f"{action} failed: {details}")


def ensure_signing_key(tools: SigningTools, keystore: Path) -> Path:
    password_file = keystore.with_suffix(".password")
    if keystore.is_file():
        if not password_file.is_file():
            raise BuildError(
                f"{keystore} exists but its password file {password_file} is missing."
            )
        return password_file
    keystore.parent.mkdir(parents=True, exist_ok=True)
    password_file.write_text(secrets.token_urlsafe(24), encoding="ascii")
    log(f"Creating signing key {keystore}")
    run_tool(
        [
            tools.keytool,
            "-genkeypair",
            "-keystore",
            keystore,
            "-storetype",
            "PKCS12",
            "-alias",
            SIGNING_KEY_ALIAS,
            "-keyalg",
            "RSA",
            "-keysize",
            "3072",
            "-validity",
            str(SIGNING_KEY_VALIDITY_DAYS),
            "-dname",
            SIGNING_KEY_DNAME,
            "-storepass:file",
            password_file,
            "-keypass:file",
            password_file,
        ],
        "Creating the signing key",
    )
    log(
        "Back up this key folder: APKs signed with another key cannot update an installed SpiritUNITE client."
    )
    return password_file


def sign_apk(
    tools: SigningTools,
    keystore: Path,
    password_file: Path,
    unsigned: Path,
    signed: Path,
) -> None:
    run_tool(
        [
            tools.java,
            "-jar",
            tools.apksigner_jar,
            "sign",
            "--ks",
            keystore,
            "--ks-key-alias",
            SIGNING_KEY_ALIAS,
            "--ks-pass",
            f"file:{password_file}",
            "--out",
            signed,
            unsigned,
        ],
        f"Signing {signed.name}",
    )
    run_tool(
        [tools.java, "-jar", tools.apksigner_jar, "verify", signed],
        f"Verifying {signed.name}",
    )


def disable_signature_shell(content: bytes) -> bytes:
    if hashlib.sha256(content).hexdigest() != SHELL_DEX_SHA256:
        raise BuildError(
            "Unsupported classes2.dex: cannot locate AnoApplication.attachBaseContextShell safely."
        )
    patched = bytearray(content)
    struct.pack_into("<H", patched, ATTACH_BASE_CONTEXT_SHELL_INSTRUCTIONS, 0x000E)
    patched[12:32] = hashlib.sha1(patched[32:]).digest()
    struct.pack_into("<I", patched, 8, zlib.adler32(patched[12:]))
    return bytes(patched)


def build_base_replacements(
    splits: dict[str, Path],
    target: HostTarget,
    old_metadata_md5: str,
    new_metadata_md5: str,
) -> dict[str, bytes]:
    with zipfile.ZipFile(splits[BASE_APK]) as base:
        replacements = {
            MSDK_CONFIG_ENTRY: patch_msdk_config(base.read(MSDK_CONFIG_ENTRY), target),
            CENTAURI_CONFIG_ENTRY: patch_centauri_config(
                base.read(CENTAURI_CONFIG_ENTRY), target
            ),
            RES_DATA_ENTRY: patch_res_data(
                base.read(RES_DATA_ENTRY), old_metadata_md5, new_metadata_md5
            ),
            SHELL_DEX_ENTRY: disable_signature_shell(base.read(SHELL_DEX_ENTRY)),
            RESOURCE_TABLE_ENTRY: rename_resource_table_package(
                base.read(RESOURCE_TABLE_ENTRY),
                OFFICIAL_PACKAGE_NAME,
                target.package_name,
            ),
        }
        manifest = drop_required_split_types(base.read(MANIFEST_ENTRY))
        if target.development:
            manifest = allow_cleartext_traffic(manifest)
        replacements[MANIFEST_ENTRY] = rename_manifest_package(
            manifest, OFFICIAL_PACKAGE_NAME, target.package_name, target.app_label
        )
        return replacements


def address_any_update_host(content: bytes) -> bytes:
    if hashlib.sha256(content).hexdigest() != GCLOUD_LIBRARY_SHA256:
        raise BuildError(
            "Unsupported libgcloud.so: cannot locate the update address service's domain check safely."
        )
    if struct.unpack_from("<I", content, IS_DOMAIN_NAME_RETURNS_DOMAIN)[0] != MOV_W20_ONE:
        raise BuildError("libgcloud.so does not hold the expected domain check instruction.")
    patched = bytearray(content)
    struct.pack_into("<I", patched, IS_DOMAIN_NAME_RETURNS_DOMAIN, MOV_W20_ZERO)
    return bytes(patched)


def build_native_replacements(
    splits: dict[str, Path], target: HostTarget
) -> tuple[dict[str, bytes], str, str, ClientBuild]:
    with zipfile.ZipFile(splits[NATIVE_APK]) as native:
        resources = native.read(RESOURCES_ARCHIVE_ENTRY)
        gcloud_library = native.read(GCLOUD_LIBRARY_ENTRY)
    patched_resources, original, patched = replace_seven_zip_file(
        resources,
        METADATA_ARCHIVE_PATH,
        lambda metadata: patch_metadata(metadata, target),
    )
    return (
        {
            RESOURCES_ARCHIVE_ENTRY: patched_resources,
            GCLOUD_LIBRARY_ENTRY: address_any_update_host(gcloud_library),
        },
        hashlib.md5(original).hexdigest(),
        hashlib.md5(patched).hexdigest(),
        client_build_for(original),
    )


def build(arguments: argparse.Namespace) -> None:
    target = resolve_target(arguments)
    splits = find_split_apks(arguments.apks.resolve())
    work_dir = arguments.work_dir.resolve()
    output_dir = (arguments.out or work_dir / "out" / target.build_name).resolve()
    work_dir.mkdir(parents=True, exist_ok=True)
    tools = find_signing_tools(work_dir, arguments.yes)
    keystore = (
        arguments.keystore or work_dir / "signing" / "spiritunite.p12"
    ).resolve()
    password_file = ensure_signing_key(tools, keystore)
    kind = "development client" if target.development else "client"
    log(
        f"Building a {kind} for {target.web_origin} (directory {target.host}:{target.dir_port})"
    )
    log(
        f"Patching the directory, update, image CDN ({target.image_cdn_address}) and voice server ({target.gvoice_server_url}) addresses in global-metadata.dat"
    )
    native_replacements, old_md5, new_md5, client_build = build_native_replacements(
        splits, target
    )
    log(f"  Client build {client_build.version_code}")
    log("Patching MSDKConfig.ini, the Centauri pay config and res.data")
    log("Disabling AnoApplication signature-shell initialization")
    log("Letting libgcloud.so reach the update server by any host name")
    if target.development:
        log("Allowing cleartext HTTP in AndroidManifest.xml")
    log(
        f"Renaming the app to {target.package_name} ({target.app_label!r}) so it installs next to the official app"
    )
    parts = [
        MergedApkPart(
            splits[BASE_APK],
            build_base_replacements(splits, target, old_md5, new_md5),
        ),
        MergedApkPart(
            splits[NATIVE_APK], native_replacements, SPLIT_ONLY_ENTRIES
        ),
        MergedApkPart(splits[ASSET_PACK_APK], {}, SPLIT_ONLY_ENTRIES),
    ]
    output_dir.mkdir(parents=True, exist_ok=True)
    apk = output_dir / f"SpiritUNITE-{target.build_name}.apk"
    with tempfile.TemporaryDirectory(dir=work_dir) as temporary:
        unsigned = Path(temporary) / apk.name
        log(f"Merging {', '.join(SPLIT_APKS)} into {apk.name} and signing it")
        merge_apks(parts, unsigned)
        sign_apk(tools, keystore, password_file, unsigned, apk)
    log("")
    install_command = f"adb install -g {apk}"
    if target.development:
        report_development_build(target, apk, install_command)
    else:
        log(f"Done. Give players {apk}")
        log(
            f"It installs as {target.package_name} next to the official app; players open the .apk on their phone to install it,"
        )
        log(f"or from a computer: {install_command}")
        log(
            f"Open TCP port {target.update_port} for the update server, and put the resource archive in resources/<productId>/ next to the server."
        )
        log(
            "If players cannot reach the HttpServer port directly, set ResourceArchive.BaseUrl to the public address that serves /resources/."
        )
        log(
            f"Put the image mirror in resources/images/ next to the server; {target.image_cdn_address} must reach the HttpServer."
        )
        log(
            f"Open UDP port {target.voice_port} for the voice server; the client sends its voice setup to {target.gvoice_server_url}."
        )
    log("")
    log("Not handled yet:")
    for change in PENDING_CLIENT_CHANGES:
        log(f"  - {change}")


def report_development_build(
    target: HostTarget, apk: Path, install_command: str
) -> None:
    log(f"Done. Development client: {apk}")
    log(
        f"It installs as {target.package_name} next to the official app. Install it on the test device:"
    )
    log(f"  {install_command}")
    if is_loopback(target.host):
        log(
            "Forward the server ports to the device after each emulator or adb restart:"
        )
        for port in dict.fromkeys(
            (target.dir_port, target.lobby_port, target.web_port, target.update_port)
        ):
            log(f"  adb reverse tcp:{port} tcp:{port}")
        if is_loopback(target.voice_host):
            log(
                f"adb reverse cannot forward the UDP voice setup to {target.gvoice_server_url}; rebuild with --voice-host set to the emulator's host gateway to reach the voice server."
            )
    else:
        log(
            f"Set TcpServer.BindAddress and HttpServer.BindAddress to 0.0.0.0 and LobbyServer.Host to {target.host} so the device can reach the server."
        )
    log(
        "This client sends passwords over plain HTTP. Use it only for local testing and never give it to players."
    )


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build a Pokemon UNITE client that connects to your SpiritUNITE server."
    )
    parser.add_argument(
        "--apks",
        type=Path,
        required=True,
        help=f"folder holding the official {', '.join(SPLIT_APKS)}",
    )
    parser.add_argument(
        "--host",
        help="your server's public domain or IPv4 address (default: LobbyServer.Host in appsettings.json)",
    )
    parser.add_argument(
        "--dir-port",
        type=int,
        help=f"directory server port (default: DirServer.Port in appsettings.json, else {DEFAULT_DIR_PORT})",
    )
    parser.add_argument(
        "--web-port",
        type=int,
        help=f"port of the login pages (default: {DEFAULT_HTTPS_PORT}; with --dev, HttpServer.Port in appsettings.json, else {DEFAULT_DEV_WEB_PORT})",
    )
    parser.add_argument(
        "--voice-host",
        help="domain or IPv4 address the client sends its voice setup to (default: VoiceServer.Host in appsettings.json, else the --host value)",
    )
    parser.add_argument(
        "--voice-port",
        type=int,
        help=f"UDP port of the voice server (default: VoiceServer.Port in appsettings.json, else {DEFAULT_VOICE_PORT})",
    )
    parser.add_argument(
        "--dev",
        action="store_true",
        help="build a local development client that talks plain HTTP to the server (no certificate or root needed)",
    )
    parser.add_argument(
        "--package",
        help=f"Android package name of the build (default: {PACKAGE_NAME_PREFIX}.<host>, or {PACKAGE_NAME_PREFIX}.{DEVELOPMENT_PACKAGE_SEGMENT}.<host> with --dev); keep it the same across rebuilds so players can update in place",
    )
    parser.add_argument(
        "--label",
        help=f"app name shown on the home screen (default: {DEFAULT_APP_LABEL})",
    )
    parser.add_argument(
        "--out",
        type=Path,
        help="output folder (default: apk-build/out/<host>, or apk-build/out/dev-<host> with --dev)",
    )
    parser.add_argument(
        "--work-dir",
        type=Path,
        default=DEFAULT_WORK_DIR,
        help="folder for the signing key, build tools and temporary files",
    )
    parser.add_argument(
        "--keystore",
        type=Path,
        help="PKCS12 signing key to use or create (default: <work-dir>/signing/spiritunite.p12)",
    )
    parser.add_argument(
        "--yes",
        action="store_true",
        help="accept the Android SDK license and download build tools without asking",
    )
    return parser.parse_args()


def main() -> int:
    try:
        build(parse_arguments())
    except BuildError as error:
        print(f"build_apk: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
