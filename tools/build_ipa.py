import argparse
import hashlib
import ipaddress
import json
import plistlib
import re
import socket
import struct
import sys
import zipfile
from collections import defaultdict
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
APPSETTINGS_PATH = REPOSITORY_ROOT / "src/SpiritUnite.Core/appsettings.json"
DEFAULT_WORK_DIR = REPOSITORY_ROOT / "ipa-build"

PAYLOAD_FOLDER = "Payload"
APP_BUNDLE_SUFFIX = ".app"
INFO_PLIST_NAME = "Info.plist"
MSDK_CONFIG_NAME = "MSDKConfig.ini"
CENTAURI_CONFIG_NAME = "centauri_oversea_cp.cfg"
CENTAURI_PLIST_NAME = "CTISDKResources-IPMappingInfo.plist"
METADATA_NAME = "global-metadata.dat"

ATS_KEY = "NSAppTransportSecurity"
ATS_ALLOWS_ARBITRARY_LOADS = "NSAllowsArbitraryLoads"
ATS_KEYS_THAT_DISABLE_ARBITRARY_LOADS = (
    "NSAllowsArbitraryLoadsInWebContent",
    "NSAllowsArbitraryLoadsForMedia",
    "NSAllowsLocalNetworking",
)
LOCAL_NETWORK_USAGE_KEY = "NSLocalNetworkUsageDescription"
LOCAL_NETWORK_USAGE_TEXT = "Connects to your SpiritUNITE development server."

MACH_O_MAGICS_LITTLE_ENDIAN = (0xFEEDFACE, 0xFEEDFACF)
FAT_MAGICS_BIG_ENDIAN = (0xCAFEBABE, 0xCAFEBABF)
MACH_O_64_MAGIC = 0xFEEDFACF
MACH_O_HEADER_32 = struct.Struct("<IiiIIII")
MACH_O_HEADER_64_SIZE = MACH_O_HEADER_32.size + 4
FAT_ARCH_32 = struct.Struct(">iiIII")
FAT_ARCH_64 = struct.Struct(">iiQQII")
LOAD_COMMAND = struct.Struct("<II")
ENCRYPTION_INFO = struct.Struct("<IIIII")
LC_ENCRYPTION_INFO = 0x21
LC_ENCRYPTION_INFO_64 = 0x2C
MACH_O_MIN_SIZE = 4096
EXECUTABLE_FOLDERS = ("", "Frameworks", "PlugIns")

LITERAL_ENTRY = struct.Struct("<II")
LITERAL_ENTRY_ALIGNMENT = 4
MIN_AGREEING_LITERALS = 2
SCANNED_CONFIG_SUFFIXES = (".ini", ".cfg", ".json", ".plist", ".txt", ".data", ".xml")
SCANNED_CONFIG_MAX_SIZE = 4 * 1024 * 1024
OFFICIAL_HOST_MARKERS = (
    "pokemon-unitepgame.com",
    "iegcom.com",
    "itopsdk.com",
    "msdkpass.com",
    "singaporepaya.com",
    "gcloudcs.com",
    "tencent.com",
    "crashsight",
    "appsflyer",
    "adjust.com",
)
LISTED_LITERAL_MAX_LENGTH = 256
COPY_CHUNK_SIZE = 8 * 1024 * 1024

DEFAULT_DIR_PORT = 10001
DEFAULT_HTTPS_PORT = 443
DEFAULT_HTTP_PORT = 80
DEFAULT_DEV_WEB_PORT = 10081
DEFAULT_VOICE_PORT = 8700
CLIENT_ADDRESS_SERVICE_PORTS = (8085, 8080)
DIR_NODE_NAME = "trunk_global_dist"
CLOUD_CONTROL_REPLACEMENT_HOST = "spiritunite.invalid"
GCLOUD_FRAMEWORK_EXECUTABLE = "Frameworks/GCloud.framework/GCloud"
GCLOUD_FRAMEWORK_SHA256 = (
    "3f946c4618cdf6e411cc4f0b83605751a00604e5cd3e1828dc613418afdb7978"
)
UNITY_FRAMEWORK_EXECUTABLE = "Frameworks/UnityFramework.framework/UnityFramework"
UNITY_FRAMEWORK_SHA256 = (
    "546c615cf2f3738f46e9431e9b6c74b078c1f272c0992a103c50188ad7998a89"
)
ACE_FRAMEWORK_EXECUTABLE = "Frameworks/anogs.framework/anogs"
ACE_FRAMEWORK_SHA256 = (
    "476a26c43481094c3914f22f823193ac31443eab91e1aeee8733a90b6664e704"
)
METADATA_SHA256 = "ab3ab88e1ca85a73b06d88165dc8dc14e434383a4f440bf27b828ee8bb9e5a86"
INIT_TSS_MODULE = 0x67AEC44
INIT_TSS_MODULE_PROLOGUE = bytes.fromhex("f44fbea9")
ACE_DID_FINISH_LAUNCHING = 0x3AD1C
ACE_DID_FINISH_LAUNCHING_PROLOGUE = bytes.fromhex("f657bda9f44f01a9")
ARM64_RETURN = bytes.fromhex("c0035fd6")
ARM64_RETURN_TRUE = bytes.fromhex("20008052c0035fd6")
IS_DOMAIN_NAME_STORES_DOMAIN = 0x327E20
MOV_W9_ONE = 0x320003E9
MOV_W9_ZERO = 0x52800009
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
HOSTNAME_LABEL = re.compile(r"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)$")
OFFICIAL_BUNDLE_IDENTIFIER = "jp.pokemon.pokemonunite"
BUNDLE_IDENTIFIER_PREFIX = "org.spiritunite"
DEVELOPMENT_BUNDLE_SEGMENT = "dev"
BUNDLE_IDENTIFIER_SEGMENT = re.compile(r"^[A-Za-z0-9-]+$")
MAX_BUNDLE_IDENTIFIER_LENGTH = 155
DEFAULT_APP_LABEL = "SpiritUNITE"
MAX_APP_LABEL_LENGTH = 50

OFFICIAL_DIR_ADDRESS = "trunk_global_dist|dirsvr.pokemon-unitepgame.com:10001"
OFFICIAL_CLOUD_CONTROL_HOST = "gcloudctrl.iegcom.com"
OFFICIAL_IMAGE_CDN_ADDRESS = "https://image.pokemon-unitepgame.com/Encrypt"
OFFICIAL_GVOICE_SERVER_URL = "udp://sg.voice.gcloudcs.com:8700"
TDM_ROUTE_HOST = "tdm.deltaverse-intl.com"
TDM_ROUTE_URL = f"https://{TDM_ROUTE_HOST}/tdm/v1/route"
CRASHSIGHT_HOST = "ios.crashsight.wetest.net"
GVOICE_CONFIG_PATH = "GCloudVoice.bundle/files/config.json"
GVOICE_DEFAULT_URLS = {
    "remoteConfigUrl": "https://hwconfig.gcloudcs.com:443",
    "logReportUrl": "https://tglog-hw.gcloudsdk.com:443",
    "voiceReportUrl": "https://harmony-global.voice.gcloudsdk.com:443",
    "voiceTranslateUrl": "https://sg.api.translator.voice.gcloudsdk.com:443",
    "voiceUrl": OFFICIAL_GVOICE_SERVER_URL,
    "flqUrl": "udp://cdn.hw.gcloudcs.com:18301",
    "dataReportUrl": "udp://qoshw.gcloudsdk.com:8011",
}
OFFICIAL_UPDATE_HOSTS = (
    "puffer.6.669535640.nishikigcloud.iegcom.com",
    "pre-puffer.5.669535640.nishikigcloud.iegcom.com",
    "download.2.669535640.nishikigcloud.iegcom.com",
    "pre-download.1.669535640.nishikigcloud.iegcom.com",
)

PENDING_CLIENT_CHANGES = (
    "The IPA is unsigned for distribution; sign it with your own provisioning before installing.",
    "SDK startup and reporting patches need on-device validation after signing.",
    "Country lookup, Firebase transport and other SDK traffic are not covered by these patches.",
    "The iOS resource archive (Puffer product 1170) must be in ResourceArchive.Directory for the update step.",
)


class BuildError(Exception):
    pass


@dataclass(frozen=True)
class HostTarget:
    host: str
    dir_port: int
    web_port: int
    development: bool
    bundle_identifier: str
    app_label: str
    voice_host: str
    voice_port: int

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
        return f"udp://{self.voice_host}:{self.voice_port}"


@dataclass(frozen=True)
class LiteralPatch:
    original: str
    replacement: Callable[[HostTarget], str]


LITERAL_PATCHES = (
    LiteralPatch(OFFICIAL_DIR_ADDRESS, lambda target: target.dir_address),
    LiteralPatch(
        OFFICIAL_CLOUD_CONTROL_HOST, lambda target: CLOUD_CONTROL_REPLACEMENT_HOST
    ),
    LiteralPatch(OFFICIAL_IMAGE_CDN_ADDRESS, lambda target: target.image_cdn_address),
    LiteralPatch(OFFICIAL_GVOICE_SERVER_URL, lambda target: target.gvoice_server_url),
    LiteralPatch(
        f"https://{CRASHSIGHT_HOST}/pb/async",
        lambda target: f"{UNREACHABLE_ORIGIN}/pb/async",
    ),
    LiteralPatch(
        "qqcollect.pgame.cn.qq.com:31010",
        lambda target: f"{CLOUD_CONTROL_REPLACEMENT_HOST}:31010",
    ),
    LiteralPatch(
        "qqcollect.pgame.cn.qq.com", lambda target: CLOUD_CONTROL_REPLACEMENT_HOST
    ),
    *(
        LiteralPatch(update_host, lambda target: target.host)
        for update_host in OFFICIAL_UPDATE_HOSTS
    ),
)


def log(message: str) -> None:
    print(message, flush=True)


def ip_address_or_none(host: str):
    try:
        return ipaddress.ip_address(host)
    except ValueError:
        return None


def is_ipv4_literal(host: str) -> bool:
    return isinstance(ip_address_or_none(host), ipaddress.IPv4Address)


def is_loopback(host: str) -> bool:
    if host.lower() == "localhost":
        return True
    address = ip_address_or_none(host)
    return address is not None and address.is_loopback


def validate_host(host: str) -> str:
    host = host.strip().rstrip(".")
    if not host:
        raise BuildError(
            "The server address is empty. Pass --host with your domain or IPv4 address."
        )
    if "://" in host or "/" in host:
        raise BuildError(
            f"--host must be a bare domain or IPv4 address, not a URL: {host}"
        )
    address = ip_address_or_none(host)
    if isinstance(address, ipaddress.IPv6Address):
        raise BuildError(
            "IPv6 addresses are not supported: the client splits the directory address on ':'. Use a domain or IPv4 address."
        )
    if address is None and (
        len(host) > 253
        or not all(HOSTNAME_LABEL.match(label) for label in host.split("."))
    ):
        raise BuildError(f"--host is not a valid domain name or IPv4 address: {host}")
    if is_loopback(host):
        raise BuildError(
            f"{host} is a loopback address, which an iPhone cannot reach. Use your PC's LAN IPv4 address with --dev, or your domain."
        )
    return host


def validate_bundle_identifier(bundle_identifier: str) -> str:
    segments = bundle_identifier.split(".")
    if (
        len(segments) < 2
        or not all(BUNDLE_IDENTIFIER_SEGMENT.match(segment) for segment in segments)
        or len(bundle_identifier) > MAX_BUNDLE_IDENTIFIER_LENGTH
    ):
        raise BuildError(
            f"{bundle_identifier!r} is not a valid bundle identifier: use at least two dot-separated parts of letters, digits and hyphens, at most {MAX_BUNDLE_IDENTIFIER_LENGTH} characters."
        )
    if bundle_identifier.lower() == OFFICIAL_BUNDLE_IDENTIFIER:
        raise BuildError(
            f"--bundle-id {OFFICIAL_BUNDLE_IDENTIFIER} is the official app's identifier; the build would replace the official app."
        )
    return bundle_identifier


def default_bundle_identifier(host: str, development: bool) -> str:
    segments = [BUNDLE_IDENTIFIER_PREFIX]
    if development:
        segments.append(DEVELOPMENT_BUNDLE_SEGMENT)
    segments.append(re.sub(r"[^A-Za-z0-9]", "-", host.lower()))
    bundle_identifier = ".".join(segments)
    if len(bundle_identifier) > MAX_BUNDLE_IDENTIFIER_LENGTH:
        raise BuildError(
            f"The bundle identifier derived from {host} is too long; pass a shorter one with --bundle-id."
        )
    return bundle_identifier


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


def validate_port(value: int, option: str) -> int:
    if not 1 <= value <= 65535:
        raise BuildError(f"{option} must be between 1 and 65535, got {value}.")
    return value


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
    port = argument if argument is not None else appsetting(settings, section, "Port")
    if port is None:
        port = default
    if not isinstance(port, int) or isinstance(port, bool):
        raise BuildError(
            f"{section}.Port in appsettings.json must be a number, got {port!r}."
        )
    return validate_port(port, option)


def resolve_target(arguments: argparse.Namespace) -> HostTarget:
    settings = read_appsettings()
    host = arguments.host or appsetting(settings, "LobbyServer", "Host")
    if not isinstance(host, str) or not host:
        raise BuildError(
            "No server address. Pass --host with your domain or IPv4 address, or set LobbyServer.Host in appsettings.json."
        )
    host = validate_host(host)
    dir_port = port_setting(
        arguments.dir_port, settings, "DirServer", DEFAULT_DIR_PORT, "--dir-port"
    )
    if arguments.dev:
        web_port = port_setting(
            arguments.web_port,
            settings,
            "HttpServer",
            DEFAULT_DEV_WEB_PORT,
            "--web-port",
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
    bundle_identifier = validate_bundle_identifier(
        arguments.bundle_id or default_bundle_identifier(host, arguments.dev)
    )
    app_label = validate_app_label(arguments.label or DEFAULT_APP_LABEL)
    voice_host = (
        arguments.voice_host or appsetting(settings, "VoiceServer", "Host") or host
    )
    if not isinstance(voice_host, str):
        raise BuildError("VoiceServer.Host must be a domain name or IPv4 address.")
    voice_host = validate_host(voice_host)
    voice_port = port_setting(
        arguments.voice_port,
        settings,
        "VoiceServer",
        DEFAULT_VOICE_PORT,
        "--voice-port",
    )
    target = HostTarget(
        host,
        dir_port,
        web_port,
        arguments.dev,
        bundle_identifier,
        app_label,
        voice_host,
        voice_port,
    )
    if len(target.gvoice_server_url) > len(OFFICIAL_GVOICE_SERVER_URL):
        raise BuildError(
            f"{target.gvoice_server_url} is too long for the client voice address. Pass --voice-host with a shorter domain or IPv4 address."
        )
    return target


def replace_ini_value(text: str, key: str, value: str) -> str:
    pattern = re.compile(rf"^({re.escape(key)}[ \t]*=[ \t]*)[^\r\n]*", re.MULTILINE)
    replaced, count = pattern.subn(lambda match: match.group(1) + value, text)
    if count != 1:
        raise BuildError(
            f"{MSDK_CONFIG_NAME} has {count} {key} lines; expected exactly one."
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


def centauri_ip_list(host: str) -> list[str]:
    if is_ipv4_literal(host):
        return [host]
    try:
        return sorted(
            {info[4][0] for info in socket.getaddrinfo(host, None, socket.AF_INET)}
        )
    except socket.gaierror:
        log(
            f"Warning: {host} does not resolve; the Centauri pay config gets an empty IP list."
        )
        return []


def patch_centauri_config(content: bytes, target: HostTarget) -> bytes:
    try:
        config = json.loads(content.decode("utf-8"))
        entries = config["config"]
    except (UnicodeDecodeError, json.JSONDecodeError, KeyError, TypeError) as error:
        raise BuildError(
            f"{CENTAURI_CONFIG_NAME} is not in the expected format: {error}"
        ) from error
    release_entries = [
        entry
        for entry in entries
        if isinstance(entry, dict) and entry.get("mode") == CENTAURI_RELEASE_MODE
    ]
    if len(release_entries) != 1:
        raise BuildError(
            f"{CENTAURI_CONFIG_NAME} has {len(release_entries)} release entries; expected exactly one."
        )
    release = release_entries[0]
    release["domain"] = target.host
    release["reportdomain"] = target.host
    release["iplist"] = centauri_ip_list(target.host)
    return (json.dumps(config, indent=4) + "\n").encode("utf-8")


def patch_centauri_plist(content: bytes, target: HostTarget) -> bytes:
    try:
        config = plistlib.loads(content)
    except (plistlib.InvalidFileException, ValueError) as error:
        raise BuildError(f"Invalid {CENTAURI_PLIST_NAME}: {error}") from error
    release = config.get(CENTAURI_RELEASE_MODE) if isinstance(config, dict) else None
    if not isinstance(release, dict) or not release:
        raise BuildError(f"{CENTAURI_PLIST_NAME} has no release region mappings.")
    ip_list = centauri_ip_list(target.host)
    for region, entry in release.items():
        if (
            not isinstance(entry, dict)
            or not isinstance(entry.get("k_domain"), str)
            or not isinstance(entry.get("k_domain_hb"), str)
            or not isinstance(entry.get("k_ip_list"), list)
        ):
            raise BuildError(f"Invalid Centauri release region {region}.")
        entry["k_domain"] = target.host
        entry["k_domain_hb"] = target.host
        entry["k_ip_list"] = ip_list
    fmt = plistlib.FMT_BINARY if content.startswith(b"bplist00") else plistlib.FMT_XML
    return plistlib.dumps(config, fmt=fmt)


@dataclass(frozen=True)
class EncryptionState:
    path: str
    encrypted_slices: int
    slices: int


@dataclass
class LiteralTable:
    data_offset: int
    entries_by_original: dict[str, list[int]]


@dataclass
class AppBundle:
    root: str
    info_plist: dict
    info_plist_format: plistlib.PlistFormat
    entries_by_name: dict[str, list[zipfile.ZipInfo]]

    @property
    def executable(self) -> str:
        return f"{self.root}{self.info_plist.get('CFBundleExecutable', '')}"

    def single(self, name: str) -> zipfile.ZipInfo | None:
        matches = self.entries_by_name.get(name, [])
        if len(matches) > 1:
            paths = ", ".join(info.filename for info in matches)
            raise BuildError(f"The IPA holds several {name} files: {paths}")
        return matches[0] if matches else None


def find_app_root(archive: zipfile.ZipFile) -> str:
    roots = {
        f"{PAYLOAD_FOLDER}/{parts[1]}/"
        for parts in (PurePosixPath(name).parts for name in archive.namelist())
        if len(parts) > 2
        and parts[0] == PAYLOAD_FOLDER
        and parts[1].endswith(APP_BUNDLE_SUFFIX)
    }
    if len(roots) != 1:
        raise BuildError(
            f"Expected exactly one {PAYLOAD_FOLDER}/<name>{APP_BUNDLE_SUFFIX} folder in the IPA, found {len(roots)}."
        )
    return roots.pop()


def open_app_bundle(archive: zipfile.ZipFile) -> AppBundle:
    root = find_app_root(archive)
    plist_name = f"{root}{INFO_PLIST_NAME}"
    try:
        plist_bytes = archive.read(plist_name)
    except KeyError as error:
        raise BuildError(f"The IPA has no {plist_name}.") from error
    try:
        info_plist = plistlib.loads(plist_bytes)
    except plistlib.InvalidFileException as error:
        raise BuildError(f"{plist_name} is not a valid property list.") from error
    plist_format = (
        plistlib.FMT_BINARY if plist_bytes.startswith(b"bplist") else plistlib.FMT_XML
    )
    entries_by_name: dict[str, list[zipfile.ZipInfo]] = defaultdict(list)
    for info in archive.infolist():
        if info.filename.startswith(root) and not info.is_dir():
            entries_by_name[PurePosixPath(info.filename).name].append(info)
    return AppBundle(root, info_plist, plist_format, dict(entries_by_name))


def read_entry_range(
    archive: zipfile.ZipFile, info: zipfile.ZipInfo, offset: int, length: int
) -> bytes:
    with archive.open(info) as stream:
        stream.seek(offset)
        return stream.read(length)


def is_executable_candidate(bundle: AppBundle, info: zipfile.ZipInfo) -> bool:
    if info.file_size < MACH_O_MIN_SIZE:
        return False
    relative = PurePosixPath(info.filename[len(bundle.root) :])
    name = relative.name
    if "." in name and not name.endswith(".dylib"):
        return False
    top = relative.parts[0] if len(relative.parts) > 1 else ""
    return top in EXECUTABLE_FOLDERS


def mach_o_slices(archive: zipfile.ZipFile, info: zipfile.ZipInfo) -> list[int] | None:
    head = read_entry_range(archive, info, 0, 8)
    if len(head) < 8:
        return None
    if struct.unpack_from("<I", head)[0] in MACH_O_MAGICS_LITTLE_ENDIAN:
        return [0]
    magic, count = struct.unpack_from(">II", head)
    if magic not in FAT_MAGICS_BIG_ENDIAN:
        return None
    arch = FAT_ARCH_64 if magic == FAT_MAGICS_BIG_ENDIAN[1] else FAT_ARCH_32
    table = read_entry_range(archive, info, 8, count * arch.size)
    return [arch.unpack_from(table, index * arch.size)[2] for index in range(count)]


def slice_is_encrypted(
    archive: zipfile.ZipFile, info: zipfile.ZipInfo, slice_offset: int
) -> bool:
    header = read_entry_range(archive, info, slice_offset, MACH_O_HEADER_64_SIZE)
    magic, _, _, _, command_count, commands_size, _ = MACH_O_HEADER_32.unpack_from(
        header
    )
    header_size = (
        MACH_O_HEADER_64_SIZE if magic == MACH_O_64_MAGIC else MACH_O_HEADER_32.size
    )
    commands = read_entry_range(
        archive, info, slice_offset + header_size, commands_size
    )
    position = 0
    for _ in range(command_count):
        if position + LOAD_COMMAND.size > len(commands):
            break
        command, command_size = LOAD_COMMAND.unpack_from(commands, position)
        if command in (LC_ENCRYPTION_INFO, LC_ENCRYPTION_INFO_64):
            crypt_id = ENCRYPTION_INFO.unpack_from(commands, position)[4]
            if crypt_id != 0:
                return True
        if command_size < LOAD_COMMAND.size:
            break
        position += command_size
    return False


def encryption_states(
    archive: zipfile.ZipFile, bundle: AppBundle
) -> list[EncryptionState]:
    states = []
    for info in archive.infolist():
        if not info.filename.startswith(bundle.root) or info.is_dir():
            continue
        if not is_executable_candidate(bundle, info):
            continue
        slices = mach_o_slices(archive, info)
        if not slices:
            continue
        encrypted = sum(slice_is_encrypted(archive, info, offset) for offset in slices)
        states.append(EncryptionState(info.filename, encrypted, len(slices)))
    return states


def find_all(data: bytes, needle: bytes) -> list[int]:
    positions = []
    position = data.find(needle)
    while position >= 0:
        positions.append(position)
        position = data.find(needle, position + 1)
    return positions


def locate_literal_table(metadata: bytes, originals: list[str]) -> LiteralTable:
    data_positions = {
        original: find_all(metadata, original.encode("utf-8")) for original in originals
    }
    present = [original for original, found in data_positions.items() if found]
    if not present:
        raise BuildError(
            f"{METADATA_NAME} holds none of the official addresses; it is not a supported client build."
        )
    originals_by_data_offset: dict[int, set[str]] = defaultdict(set)
    entries_by_data_offset: dict[int, dict[str, list[int]]] = defaultdict(
        lambda: defaultdict(list)
    )
    for original in present:
        length_bytes = struct.pack("<I", len(original.encode("utf-8")))
        for entry in find_all(metadata, length_bytes):
            if entry % LITERAL_ENTRY_ALIGNMENT or entry + LITERAL_ENTRY.size > len(
                metadata
            ):
                continue
            relative = struct.unpack_from("<I", metadata, entry + 4)[0]
            for position in data_positions[original]:
                if position >= relative:
                    data_offset = position - relative
                    originals_by_data_offset[data_offset].add(original)
                    entries_by_data_offset[data_offset][original].append(entry)
    if not originals_by_data_offset:
        raise BuildError(f"Could not find the string literal table in {METADATA_NAME}.")
    best = max(
        originals_by_data_offset,
        key=lambda offset: len(originals_by_data_offset[offset]),
    )
    if len(originals_by_data_offset[best]) < MIN_AGREEING_LITERALS:
        raise BuildError(
            f"Could not find the string literal table in {METADATA_NAME}; fewer than {MIN_AGREEING_LITERALS} official addresses share one table."
        )
    return LiteralTable(best, dict(entries_by_data_offset[best]))


def literal_entry(metadata: bytes, entry: int) -> tuple[int, int]:
    return LITERAL_ENTRY.unpack_from(metadata, entry)


def contiguous_literal_entries(metadata: bytes, table: LiteralTable) -> range:
    anchor = next(
        entry for entries in table.entries_by_original.values() for entry in entries
    )

    def follows(previous: int, current: int) -> bool:
        length, relative = literal_entry(metadata, previous)
        return relative + length == literal_entry(metadata, current)[1]

    first = anchor
    while first - LITERAL_ENTRY.size >= 0 and follows(
        first - LITERAL_ENTRY.size, first
    ):
        first -= LITERAL_ENTRY.size
    last = anchor
    while last + 2 * LITERAL_ENTRY.size <= len(metadata) and follows(
        last, last + LITERAL_ENTRY.size
    ):
        last += LITERAL_ENTRY.size
    return range(first, last + LITERAL_ENTRY.size, LITERAL_ENTRY.size)


def official_host_literals(metadata: bytes, table: LiteralTable) -> list[str]:
    found = []
    for entry in contiguous_literal_entries(metadata, table):
        length, relative = literal_entry(metadata, entry)
        if length > LISTED_LITERAL_MAX_LENGTH:
            continue
        start = table.data_offset + relative
        try:
            text = metadata[start : start + length].decode("utf-8")
        except UnicodeDecodeError:
            continue
        if any(marker in text.lower() for marker in OFFICIAL_HOST_MARKERS):
            found.append(text)
    return found


def write_literal(
    metadata: bytearray, table: LiteralTable, entry: int, original: str, value: str
) -> None:
    length, relative = literal_entry(metadata, entry)
    data_offset = table.data_offset + relative
    if bytes(metadata[data_offset : data_offset + length]) != original.encode("utf-8"):
        raise BuildError(
            f"{METADATA_NAME} slot for {original!r} changed while patching."
        )
    encoded = value.encode("utf-8")
    if len(encoded) > length:
        raise BuildError(
            f"{value!r} is {len(encoded)} bytes; the client slot for {original!r} holds at most {length}. Use a shorter domain."
        )
    metadata[data_offset : data_offset + length] = encoded.ljust(length, b"\x00")
    struct.pack_into("<I", metadata, entry, len(encoded))


def patch_metadata(metadata: bytes, target: HostTarget) -> bytes:
    if hashlib.sha256(metadata).hexdigest() != METADATA_SHA256:
        raise BuildError(
            "Unsupported global-metadata.dat: SDK defaults cannot be patched safely."
        )
    originals = [patch.original for patch in LITERAL_PATCHES]
    table = locate_literal_table(metadata, originals)
    missing = [
        original for original in originals if original not in table.entries_by_original
    ]
    if missing:
        raise BuildError(
            f"{METADATA_NAME} is missing these official addresses, so this build cannot be patched safely: {', '.join(missing)}"
        )
    patched = bytearray(metadata)
    for patch in LITERAL_PATCHES:
        for entry in table.entries_by_original[patch.original]:
            write_literal(
                patched, table, entry, patch.original, patch.replacement(target)
            )
    content = silence_host(bytes(patched), TDM_ROUTE_HOST, 1)
    return silence_host(content, CRASHSIGHT_HOST, 1)


def silence_host(content: bytes, host: str, expected_count: int) -> bytes:
    prefix_length = len(host) - len(".invalid")
    if prefix_length < 1:
        raise BuildError(f"Host {host} has no space for an .invalid address.")
    replacement = (
        host[:prefix_length].rstrip(".-").ljust(prefix_length, "x") + ".invalid"
    )
    original = host.encode("ascii")
    if content.count(original) != expected_count:
        raise BuildError(f"Expected {expected_count} copies of {host}.")
    return content.replace(original, replacement.encode("ascii"))


def patch_gvoice_config(content: bytes, target: HostTarget) -> bytes:
    try:
        config = json.loads(content)
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise BuildError(f"Invalid {GVOICE_CONFIG_PATH}: {error}") from error
    visitor = config.get("cdnvister") if isinstance(config, dict) else None
    urls = visitor.get("default_url") if isinstance(visitor, dict) else None
    if urls != GVOICE_DEFAULT_URLS:
        raise BuildError(f"Unsupported {GVOICE_CONFIG_PATH} default URLs.")
    for key, original in GVOICE_DEFAULT_URLS.items():
        if key == "voiceUrl":
            urls[key] = target.gvoice_server_url
        else:
            scheme, authority = original.split("://", 1)
            port = authority.rsplit(":", 1)[1]
            urls[key] = f"{scheme}://{CLOUD_CONTROL_REPLACEMENT_HOST}:{port}"
    return (json.dumps(config, indent=4) + "\n").encode("utf-8")


def allow_development_http(info_plist: dict) -> dict:
    patched = dict(info_plist)
    transport = dict(patched.get(ATS_KEY) or {})
    for key in ATS_KEYS_THAT_DISABLE_ARBITRARY_LOADS:
        transport.pop(key, None)
    transport[ATS_ALLOWS_ARBITRARY_LOADS] = True
    patched[ATS_KEY] = transport
    patched.setdefault(LOCAL_NETWORK_USAGE_KEY, LOCAL_NETWORK_USAGE_TEXT)
    return patched


def rename_bundle(plist: dict, target: HostTarget) -> dict:
    if plist.get("CFBundleIdentifier") != OFFICIAL_BUNDLE_IDENTIFIER:
        raise BuildError(
            f"Info.plist CFBundleIdentifier is {plist.get('CFBundleIdentifier')!r}, not {OFFICIAL_BUNDLE_IDENTIFIER}."
        )
    plist["CFBundleIdentifier"] = target.bundle_identifier
    plist["CFBundleDisplayName"] = target.app_label
    url_types = plist.get("CFBundleURLTypes") or []
    if not isinstance(url_types, list):
        raise BuildError("Info.plist CFBundleURLTypes is not a list.")
    plist["CFBundleURLTypes"] = [
        {**url_type, "CFBundleURLName": target.bundle_identifier}
        if isinstance(url_type, dict)
        and url_type.get("CFBundleURLName") == OFFICIAL_BUNDLE_IDENTIFIER
        else url_type
        for url_type in url_types
    ]
    return plist


def patch_info_plist(info_plist: dict, target: HostTarget) -> dict:
    plist = rename_bundle(dict(info_plist), target)
    tdm_config = plist.get("TDM")
    if (
        not isinstance(tdm_config, dict)
        or tdm_config.get("TGEMIT_ROUTER_ADDRESS_FORMAL") != TDM_ROUTE_URL
    ):
        raise BuildError("Info.plist TDM route is not the expected official URL.")
    plist["TDM"] = {
        **tdm_config,
        "TGEMIT_ROUTER_ADDRESS_FORMAL": f"{UNREACHABLE_ORIGIN}/tdm/v1/route",
    }
    cloud_config = plist.get("GCloudCore")
    if cloud_config is not None:
        if not isinstance(cloud_config, dict):
            raise BuildError("Info.plist GCloudCore is not a dictionary.")
        cloud_config = dict(cloud_config)
        for key in ("RemoteConfigUrl", "GTraceUrl"):
            value = cloud_config.get(key)
            if not isinstance(value, str) or OFFICIAL_CLOUD_CONTROL_HOST not in value:
                raise BuildError(
                    f"Info.plist GCloudCore.{key} is not the expected official URL."
                )
            cloud_config[key] = value.replace(
                OFFICIAL_CLOUD_CONTROL_HOST, CLOUD_CONTROL_REPLACEMENT_HOST
            )
        plist["GCloudCore"] = cloud_config
    if target.development:
        plist = allow_development_http(plist)
    return plist


def config_entries(archive: zipfile.ZipFile, bundle: AppBundle):
    for infos in bundle.entries_by_name.values():
        for info in infos:
            if (
                info.filename.lower().endswith(SCANNED_CONFIG_SUFFIXES)
                and info.file_size <= SCANNED_CONFIG_MAX_SIZE
            ):
                yield info, archive.read(info)


def entries_referencing(
    archive: zipfile.ZipFile, bundle: AppBundle, needle: bytes
) -> list[zipfile.ZipInfo]:
    return [
        info for info, content in config_entries(archive, bundle) if needle in content
    ]


def inspect(ipa: Path) -> None:
    with zipfile.ZipFile(ipa) as archive:
        bundle = open_app_bundle(archive)
        plist = bundle.info_plist
        log(f"App bundle:   {bundle.root}")
        for key in (
            "CFBundleIdentifier",
            "CFBundleShortVersionString",
            "CFBundleVersion",
            "MinimumOSVersion",
        ):
            log(f"{key + ':':<28}{plist.get(key)}")
        log(f"{ATS_KEY + ':':<28}{plist.get(ATS_KEY)}")
        schemes = [
            scheme
            for url_type in plist.get("CFBundleURLTypes", [])
            for scheme in url_type.get("CFBundleURLSchemes", [])
        ]
        log(f"{'URL schemes:':<28}{', '.join(schemes) or 'none'}")

        log("\nMach-O encryption (FairPlay cryptid):")
        for state in encryption_states(archive, bundle):
            status = "ENCRYPTED" if state.encrypted_slices else "decrypted"
            log(f"  {status:<10} {state.path} ({state.slices} slice(s))")

        log("\nClient files:")
        for name in (
            MSDK_CONFIG_NAME,
            CENTAURI_CONFIG_NAME,
            CENTAURI_PLIST_NAME,
            METADATA_NAME,
        ):
            for info in bundle.entries_by_name.get(name, []):
                log(f"  {info.filename} ({info.file_size} bytes)")
            if name not in bundle.entries_by_name:
                log(f"  {name}: not found")

        metadata_info = bundle.single(METADATA_NAME)
        if metadata_info is not None:
            metadata = archive.read(metadata_info)
            metadata_md5 = hashlib.md5(metadata).hexdigest()
            log(f"\n{METADATA_NAME} md5 {metadata_md5}")
            log(f"  header word 0x{struct.unpack_from('<I', metadata)[0]:08X}")
            table = locate_literal_table(
                metadata, [patch.original for patch in LITERAL_PATCHES]
            )
            log(f"  string literal data at 0x{table.data_offset:X}")
            for patch in LITERAL_PATCHES:
                entries = table.entries_by_original.get(patch.original, [])
                where = ", ".join(f"0x{entry:X}" for entry in entries) or "NOT FOUND"
                log(f"  {patch.original}: {where}")
            log("  Literals naming official or third-party hosts:")
            for text in official_host_literals(metadata, table):
                log(f"    {text}")
            references = entries_referencing(archive, bundle, metadata_md5.encode())
            for info in references:
                log(f"  md5 referenced by {info.filename}")

        log("\nConfig files naming official or third-party hosts:")
        for info, content in config_entries(archive, bundle):
            lowered = content.lower()
            markers = [m for m in OFFICIAL_HOST_MARKERS if m.encode() in lowered]
            if markers:
                log(f"  {info.filename}: {', '.join(markers)}")


def address_any_update_host(content: bytes) -> bytes:
    if hashlib.sha256(content).hexdigest() != GCLOUD_FRAMEWORK_SHA256:
        raise BuildError(
            "Unsupported GCloud framework: cannot locate the update address service's domain check safely."
        )
    if struct.unpack_from("<I", content, IS_DOMAIN_NAME_STORES_DOMAIN)[0] != MOV_W9_ONE:
        raise BuildError(
            "The GCloud framework does not hold the expected domain check instruction."
        )
    patched = bytearray(content)
    struct.pack_into("<I", patched, IS_DOMAIN_NAME_STORES_DOMAIN, MOV_W9_ZERO)
    return bytes(patched)


def patch_framework_instruction(
    content: bytes,
    executable: str,
    expected_sha256: str,
    offset: int,
    original: bytes,
    replacement: bytes,
) -> bytes:
    if hashlib.sha256(content).hexdigest() != expected_sha256:
        raise BuildError(
            f"Unsupported {executable}: ACE startup cannot be patched safely."
        )
    if (
        len(original) != len(replacement)
        or content[offset : offset + len(original)] != original
    ):
        raise BuildError(
            f"{executable} does not hold the expected ACE startup instructions."
        )
    patched = bytearray(content)
    patched[offset : offset + len(original)] = replacement
    return bytes(patched)


def build_replacements(
    archive: zipfile.ZipFile, bundle: AppBundle, target: HostTarget
) -> dict[str, bytes]:
    encrypted = [
        state.path
        for state in encryption_states(archive, bundle)
        if state.encrypted_slices
    ]
    if encrypted:
        raise BuildError(
            "These executables are still FairPlay encrypted and will not launch once re-signed; use a decrypted IPA: "
            + ", ".join(encrypted)
        )
    replacements: dict[str, bytes] = {}

    msdk_config = bundle.single(MSDK_CONFIG_NAME)
    if msdk_config is None:
        raise BuildError(
            f"The IPA has no {MSDK_CONFIG_NAME}; the login addresses cannot be changed."
        )
    replacements[msdk_config.filename] = patch_msdk_config(
        archive.read(msdk_config), target
    )
    log(f"Patched {msdk_config.filename}")

    centauri_config = bundle.single(CENTAURI_CONFIG_NAME)
    centauri_plist = bundle.single(CENTAURI_PLIST_NAME)
    if centauri_config is None and centauri_plist is None:
        raise BuildError("The IPA has no supported Centauri host mapping.")
    if centauri_config is not None:
        replacements[centauri_config.filename] = patch_centauri_config(
            archive.read(centauri_config), target
        )
        log(f"Patched {centauri_config.filename}")
    if centauri_plist is not None:
        replacements[centauri_plist.filename] = patch_centauri_plist(
            archive.read(centauri_plist), target
        )
        log(f"Patched {centauri_plist.filename}")

    metadata_info = bundle.single(METADATA_NAME)
    if metadata_info is None:
        raise BuildError(f"The IPA has no {METADATA_NAME}.")
    metadata = archive.read(metadata_info)
    patched_metadata = patch_metadata(metadata, target)
    replacements[metadata_info.filename] = patched_metadata
    log(f"Patched {metadata_info.filename}")

    old_md5 = hashlib.md5(metadata).hexdigest().encode()
    new_md5 = hashlib.md5(patched_metadata).hexdigest().encode()
    for info in entries_referencing(archive, bundle, old_md5):
        replacements[info.filename] = archive.read(info).replace(old_md5, new_md5)
        log(f"Updated the {METADATA_NAME} md5 in {info.filename}")

    gcloud_name = f"{bundle.root}{GCLOUD_FRAMEWORK_EXECUTABLE}"
    if gcloud_name not in archive.NameToInfo:
        raise BuildError(f"The IPA has no {GCLOUD_FRAMEWORK_EXECUTABLE}.")
    replacements[gcloud_name] = address_any_update_host(archive.read(gcloud_name))
    log(f"Patched {gcloud_name} so the update server is reached by any host name")

    for executable, digest, offset, original, replacement in (
        (
            UNITY_FRAMEWORK_EXECUTABLE,
            UNITY_FRAMEWORK_SHA256,
            INIT_TSS_MODULE,
            INIT_TSS_MODULE_PROLOGUE,
            ARM64_RETURN,
        ),
        (
            ACE_FRAMEWORK_EXECUTABLE,
            ACE_FRAMEWORK_SHA256,
            ACE_DID_FINISH_LAUNCHING,
            ACE_DID_FINISH_LAUNCHING_PROLOGUE,
            ARM64_RETURN_TRUE,
        ),
    ):
        name = f"{bundle.root}{executable}"
        if name not in archive.NameToInfo:
            raise BuildError(f"The IPA has no {executable}.")
        replacements[name] = patch_framework_instruction(
            archive.read(name), executable, digest, offset, original, replacement
        )
        log(f"Patched {name} to skip ACE startup")

    gvoice_name = f"{bundle.root}{GVOICE_CONFIG_PATH}"
    if gvoice_name not in archive.NameToInfo:
        raise BuildError(f"The IPA has no {GVOICE_CONFIG_PATH}.")
    replacements[gvoice_name] = patch_gvoice_config(archive.read(gvoice_name), target)
    log(f"Patched SDK reporting defaults and voice server {target.gvoice_server_url}")

    plist_name = f"{bundle.root}{INFO_PLIST_NAME}"
    replacements[plist_name] = plistlib.dumps(
        patch_info_plist(bundle.info_plist, target), fmt=bundle.info_plist_format
    )
    log(
        f"Patched {plist_name}: bundle identifier {target.bundle_identifier}, name {target.app_label!r}"
    )
    return replacements


def copied_info(info: zipfile.ZipInfo) -> zipfile.ZipInfo:
    copy = zipfile.ZipInfo(info.filename, info.date_time)
    copy.compress_type = info.compress_type
    copy.external_attr = info.external_attr
    copy.create_system = info.create_system
    copy.comment = info.comment
    return copy


def rewrite_ipa(
    source: Path, destination: Path, replacements: dict[str, bytes]
) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    partial = destination.with_suffix(destination.suffix + ".partial")
    with (
        zipfile.ZipFile(source) as archive,
        zipfile.ZipFile(partial, "w", allowZip64=True) as output,
    ):
        for info in archive.infolist():
            target_info = copied_info(info)
            if info.filename in replacements:
                output.writestr(target_info, replacements[info.filename])
                continue
            with (
                archive.open(info) as reader,
                output.open(target_info, "w", force_zip64=True) as writer,
            ):
                while chunk := reader.read(COPY_CHUNK_SIZE):
                    writer.write(chunk)
    partial.replace(destination)


def build(arguments: argparse.Namespace) -> None:
    if not arguments.ipa.is_file():
        raise BuildError(f"IPA not found: {arguments.ipa}")
    if arguments.inspect:
        inspect(arguments.ipa)
        return
    target = resolve_target(arguments)
    if is_ipv4_literal(target.host) and not target.development:
        log(
            "Warning: a release IPA on a bare IP cannot use trusted HTTPS for the login pages; use a domain."
        )
    out_dir = arguments.out or DEFAULT_WORK_DIR / "out" / target.build_name
    destination = out_dir / f"SpiritUNITE-{target.build_name}.ipa"
    with zipfile.ZipFile(arguments.ipa) as archive:
        bundle = open_app_bundle(archive)
        replacements = build_replacements(archive, bundle, target)
    log(f"Writing {destination}")
    rewrite_ipa(arguments.ipa, destination, replacements)
    log(f"\nBuilt {destination} for {target.dir_address}.")
    log(
        f"Sign it with your own Apple provisioning for {target.bundle_identifier} (for example Sideloadly, AltStore or zsign); it installs next to the official app."
    )
    log("Not handled yet:")
    for change in PENDING_CLIENT_CHANGES:
        log(f"  - {change}")


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build an iOS Pokemon UNITE client that connects to your SpiritUNITE server."
    )
    parser.add_argument(
        "--ipa",
        type=Path,
        required=True,
        help="official IPA; encrypted input supports --inspect, builds require decryption",
    )
    parser.add_argument(
        "--inspect",
        action="store_true",
        help="report what the IPA holds (encryption, config files, address literals) without building",
    )
    parser.add_argument(
        "--host",
        help="your server's public domain or IPv4 address (default: LobbyServer.Host in appsettings.json)",
    )
    parser.add_argument(
        "--dir-port", type=int, help="directory server port (default: DirServer.Port)"
    )
    parser.add_argument(
        "--web-port",
        type=int,
        help="port of the login pages (default: 443; with --dev, HttpServer.Port)",
    )
    parser.add_argument(
        "--dev",
        action="store_true",
        help="build a development client that talks plain HTTP to a server on your LAN",
    )
    parser.add_argument(
        "--voice-host",
        help="voice server domain or IPv4 address (default: VoiceServer.Host, or --host)",
    )
    parser.add_argument(
        "--voice-port",
        type=int,
        help="voice server UDP port (default: VoiceServer.Port, or 8700)",
    )
    parser.add_argument(
        "--bundle-id",
        help=f"bundle identifier of the build (default: {BUNDLE_IDENTIFIER_PREFIX}.<host>, or {BUNDLE_IDENTIFIER_PREFIX}.{DEVELOPMENT_BUNDLE_SEGMENT}.<host> with --dev); keep it the same across rebuilds so players can update in place",
    )
    parser.add_argument(
        "--label",
        help=f"app name shown on the home screen (default: {DEFAULT_APP_LABEL})",
    )
    parser.add_argument(
        "--out",
        type=Path,
        help="output folder (default: ipa-build/out/<host>, or ipa-build/out/dev-<host> with --dev)",
    )
    return parser.parse_args()


def main() -> int:
    try:
        build(parse_arguments())
    except BuildError as error:
        print(f"build_ipa: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
