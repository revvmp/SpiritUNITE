import argparse
import concurrent.futures
import hashlib
import http.client
import ipaddress
import json
import os
import re
import socket
import struct
import sys
import threading
import time
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from urllib.parse import urlsplit

REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
APPSETTINGS_PATH = REPOSITORY_ROOT / "src/SpiritUnite.Core/appsettings.json"
IMAGE_FILE_LIST_PATH = Path(__file__).resolve().parent / "image_cdn_files.json"
GALLERY_FILE_LIST_PATH = Path(__file__).resolve().parent / "gallery_files.json"
DEFAULT_RESOURCE_DIRECTORY = "resources"
IMAGE_FOLDER_NAME = "images"
GALLERY_FOLDER_NAME = "gallery"

GAME_ID = 669535640
GAME_KEY = "fc4db4cb2a243e3248392496b7c939ba"
CLIENT_APP_VERSION = "1.25.1.0"
ADDRESS_SERVICE_PORT = 8085
TUAP_IV = bytes(range(16))
MAX_TUAP_BODY = 65536
MAX_DECOMPRESSED_REPLY = 1024 * 1024
SOCKET_TIMEOUT_SECONDS = 20

ARCHIVE_CDN_HOST = "gcloud.pokemon-unitepgame.com"
ARCHIVE_RANGE_BYTES = 16 * 1024 * 1024
ARCHIVE_READ_BYTES = 1024 * 1024
ARCHIVE_TIMEOUT_SECONDS = 60
ARCHIVE_RANGE_ATTEMPTS = 5

IMAGE_CDN_HOST = "image.pokemon-unitepgame.com"
IMAGE_CDN_ROOT = "/Encrypt"
IMAGE_INDEX_FILE = "cdn_hash_md5_size.bytes"
IMAGE_USER_AGENT = "UnityPlayer"
IMAGE_TIMEOUT_SECONDS = 30
IMAGE_REQUEST_ATTEMPTS = 4
IMAGE_MAX_WORKERS = 8
IMAGE_PROGRESS_EVERY = 500
IMAGE_RELATIVE_PATH = re.compile(r"[A-Za-z]+(/[A-Za-z0-9_\-]+)+/[A-Za-z0-9_\-.]+\.bytes")
GALLERY_HOST = "www.pokemon-unitepgame.com"
GALLERY_ROOT = "/gallery"
GALLERY_USER_AGENT = "Mozilla/5.0"
GALLERY_RELATIVE_PATH = re.compile(r"[A-Za-z0-9_\-]+(/[A-Za-z0-9_\-.]+)*\.(html|css|js|png|jpg)")
MD5_HEX = re.compile(r"[0-9a-f]{32}")
HASH_READ_BYTES = 4 * 1024 * 1024


@dataclass(frozen=True)
class Platform:
    name: str
    puffer_product_id: int
    dolphin_product_id: int


PLATFORMS = {
    "android": Platform("android", 1169, 3215),
    "ios": Platform("ios", 1170, 3216),
}


class FetchError(Exception):
    pass


def multiply_in_field(left, right):
    product = 0
    while right:
        if right & 1:
            product ^= left
        left = ((left << 1) ^ 0x11B) if left & 0x80 else left << 1
        right >>= 1
    return product


def build_substitution_boxes():
    forward = [0] * 256
    for value in range(256):
        inverse_value = next((candidate for candidate in range(1, 256) if multiply_in_field(value, candidate) == 1), 0)
        transformed = inverse_value
        for shift in range(1, 5):
            transformed ^= ((inverse_value << shift) | (inverse_value >> (8 - shift))) & 0xFF
        forward[value] = transformed ^ 0x63
    backward = [0] * 256
    for value, substituted in enumerate(forward):
        backward[substituted] = value
    return forward, backward


SUBSTITUTION, INVERSE_SUBSTITUTION = build_substitution_boxes()
FIELD_PRODUCTS = {factor: [multiply_in_field(value, factor) for value in range(256)] for factor in (2, 3, 9, 11, 13, 14)}
MIX_FACTORS = ((2, 3, 1, 1), (1, 2, 3, 1), (1, 1, 2, 3), (3, 1, 1, 2))
INVERSE_MIX_FACTORS = ((14, 11, 13, 9), (9, 14, 11, 13), (13, 9, 14, 11), (11, 13, 9, 14))
AES_ROUNDS = 10
AES_BLOCK_BYTES = 16


def field_product(value, factor):
    return value if factor == 1 else FIELD_PRODUCTS[factor][value]


class Aes128:
    def __init__(self, key):
        if len(key) != AES_BLOCK_BYTES:
            raise FetchError("The TUAP session key is not 16 bytes")
        words = [list(key[index : index + 4]) for index in range(0, 16, 4)]
        round_constant = 1
        while len(words) < 4 * (AES_ROUNDS + 1):
            word = list(words[-1])
            if len(words) % 4 == 0:
                word = [SUBSTITUTION[byte] for byte in word[1:] + word[:1]]
                word[0] ^= round_constant
                round_constant = multiply_in_field(round_constant, 2)
            words.append([left ^ right for left, right in zip(words[-4], word)])
        self.round_keys = [sum(words[4 * round_index : 4 * round_index + 4], []) for round_index in range(AES_ROUNDS + 1)]

    @staticmethod
    def add_round_key(state, round_key):
        return [value ^ key for value, key in zip(state, round_key)]

    @staticmethod
    def shift_rows(state, direction):
        return [state[row + 4 * ((column + direction * row) % 4)] for column in range(4) for row in range(4)]

    @staticmethod
    def mix_columns(state, factors):
        mixed = []
        for column in range(4):
            values = state[4 * column : 4 * column + 4]
            for row_factors in factors:
                result = 0
                for value, factor in zip(values, row_factors):
                    result ^= field_product(value, factor)
                mixed.append(result)
        return mixed

    def encrypt_block(self, block):
        state = self.add_round_key(list(block), self.round_keys[0])
        for round_index in range(1, AES_ROUNDS + 1):
            state = self.shift_rows([SUBSTITUTION[value] for value in state], 1)
            if round_index != AES_ROUNDS:
                state = self.mix_columns(state, MIX_FACTORS)
            state = self.add_round_key(state, self.round_keys[round_index])
        return bytes(state)

    def decrypt_block(self, block):
        state = self.add_round_key(list(block), self.round_keys[AES_ROUNDS])
        for round_index in range(AES_ROUNDS - 1, -1, -1):
            state = [INVERSE_SUBSTITUTION[value] for value in self.shift_rows(state, -1)]
            state = self.add_round_key(state, self.round_keys[round_index])
            if round_index:
                state = self.mix_columns(state, INVERSE_MIX_FACTORS)
        return bytes(state)

    def encrypt_cbc(self, plain, iv):
        previous, output = iv, bytearray()
        for offset in range(0, len(plain), AES_BLOCK_BYTES):
            previous = self.encrypt_block(bytes(a ^ b for a, b in zip(plain[offset : offset + AES_BLOCK_BYTES], previous)))
            output += previous
        return bytes(output)

    def decrypt_cbc(self, cipher, iv):
        previous, output = iv, bytearray()
        for offset in range(0, len(cipher), AES_BLOCK_BYTES):
            block = cipher[offset : offset + AES_BLOCK_BYTES]
            output += bytes(a ^ b for a, b in zip(self.decrypt_block(block), previous))
            previous = block
        return bytes(output)


class Reader:
    def __init__(self, data):
        self.data = data
        self.position = 0

    def take(self, size):
        if size < 0 or self.position + size > len(self.data):
            raise FetchError("Update server reply is truncated or has an invalid length")
        result = self.data[self.position : self.position + size]
        self.position += size
        return result

    def number(self, size, signed=False):
        return int.from_bytes(self.take(size), "big", signed=signed)

    def varint(self):
        result = 0
        for shift in range(0, 70, 7):
            value = self.number(1)
            result |= (value & 127) << shift
            if value < 128:
                if result >= 1 << 64:
                    break
                return result
        raise FetchError("Update server reply has an invalid varint")


def encode_varint(value):
    result = bytearray()
    while value >= 128:
        result.append((value & 127) | 128)
        value >>= 7
    result.append(value)
    return bytes(result)


def tlv(*fields):
    result = bytearray()
    for tag, kind, value in fields:
        result += encode_varint((tag << 4) | kind)
        if kind == 0:
            result += encode_varint(value)
        elif kind == 1:
            result.append(value)
        else:
            result += struct.pack(">I", len(value)) + value
    return bytes(result)


def read_tlv(data):
    reader = Reader(data)
    result = {}
    while reader.position < len(data):
        key = reader.varint()
        tag, kind = key >> 4, key & 15
        if tag in result:
            raise FetchError("Update server reply repeats a TLV field")
        if kind == 0:
            value = reader.varint()
        elif kind in (1, 2, 3, 4):
            value = reader.number(1 << (kind - 1))
        elif kind == 5:
            value = reader.take(reader.number(4))
        else:
            raise FetchError("Update server reply has an unknown TLV type")
        result[tag] = value
    return result


def nested_tlv(data, *tags):
    for tag in tags:
        fields = read_tlv(data)
        if tag not in fields:
            raise FetchError("Update server reply is missing a TLV field")
        data = fields[tag]
    return data


def frame(data):
    return b"\xaa" + struct.pack(">I", len(data) + 5) + data


def unframe(data):
    reader = Reader(data)
    if reader.take(1) != b"\xaa" or reader.number(4) != len(data):
        raise FetchError("Update server reply has an invalid TLV frame")
    return reader.take(len(data) - 5)


def service_route(service):
    return tlv((4, 5, tlv((1, 5, service.encode("ascii")))))


SERVICE_SYN_KIND = {"QueryAddrSvr": 2, "PufferUpdateService": 12}


def syn_section(service):
    account = tlv((1, 0, 4099), (2, 1, 3), (3, 5, tlv((3, 5, b"gcloud-none-0000"))), (4, 0, 0))
    return tlv(
        (1, 1, 0),
        (2, 5, b"\x01\x00"),
        (3, 1, 0),
        (4, 0, 1339071280),
        (5, 0, SERVICE_SYN_KIND[service]),
        (6, 0, 202),
        (7, 5, account),
        (8, 1, 0),
        (10, 1, 0),
        (12, 1, 1),
        (13, 1, 4),
        (14, 5, service_route(service)),
        (15, 0, 0),
    )


def decompress_lz4_block(data):
    reader = Reader(data)
    result = bytearray()

    def extended_length(value):
        if value == 15:
            while True:
                extra = reader.number(1)
                value += extra
                if value > MAX_DECOMPRESSED_REPLY:
                    raise FetchError("Update server reply decompresses beyond the limit")
                if extra != 255:
                    break
        return value

    while reader.position < len(data):
        token = reader.number(1)
        literal_length = extended_length(token >> 4)
        if len(result) + literal_length > MAX_DECOMPRESSED_REPLY:
            raise FetchError("Update server reply decompresses beyond the limit")
        result += reader.take(literal_length)
        if reader.position == len(data):
            return bytes(result)
        offset = int.from_bytes(reader.take(2), "little")
        match_length = extended_length(token & 15) + 4
        if not 0 < offset <= len(result) or len(result) + match_length > MAX_DECOMPRESSED_REPLY:
            raise FetchError("Update server reply has an invalid LZ4 match")
        for _ in range(match_length):
            result.append(result[-offset])
    return bytes(result)


def thrift_struct(*fields):
    result = bytearray()
    for tag, kind, value in fields:
        result += struct.pack(">Bh", kind, tag)
        if kind in (3, 8, 10):
            result += value.to_bytes({3: 1, 8: 4, 10: 8}[kind], "big", signed=True)
        elif kind == 11:
            encoded = value.encode("utf-8")
            result += struct.pack(">I", len(encoded)) + encoded
        else:
            result += value
    return bytes(result) + b"\x00"


THRIFT_MAX_DEPTH = 24
THRIFT_MAX_ELEMENTS = 10000


def read_thrift(reader, kind, depth=0):
    if depth > THRIFT_MAX_DEPTH:
        raise FetchError("Update server reply nests too deeply")
    if kind in (2, 3, 6, 8, 10):
        return reader.number({2: 1, 3: 1, 6: 2, 8: 4, 10: 8}[kind], signed=True)
    if kind == 4:
        return struct.unpack(">d", reader.take(8))[0]
    if kind == 11:
        value = reader.take(reader.number(4, signed=True))
        try:
            return value.decode("utf-8")
        except UnicodeDecodeError:
            return value
    if kind == 12:
        result = {}
        while True:
            field_kind = reader.number(1)
            if field_kind == 0:
                return result
            tag = reader.number(2, signed=True)
            if tag in result:
                raise FetchError("Update server reply repeats a Thrift field")
            result[tag] = read_thrift(reader, field_kind, depth + 1)
    if kind in (14, 15):
        element_kind, count = reader.number(1), reader.number(4, signed=True)
        if not 0 <= count <= THRIFT_MAX_ELEMENTS:
            raise FetchError("Update server reply has an oversized list")
        return [read_thrift(reader, element_kind, depth + 1) for _ in range(count)]
    if kind == 13:
        key_kind, value_kind = reader.number(1), reader.number(1)
        count = reader.number(4, signed=True)
        if not 0 <= count <= THRIFT_MAX_ELEMENTS:
            raise FetchError("Update server reply has an oversized map")
        return [[read_thrift(reader, key_kind, depth + 1), read_thrift(reader, value_kind, depth + 1)] for _ in range(count)]
    raise FetchError(f"Update server reply has an unknown Thrift type {kind}")


TUAP_SYN = 0x1001
TUAP_ACK = 0x1002
TUAP_AUTH = 0x2001
TUAP_AUTH_REPLY = 0x2002
TUAP_BINGO = 0x6002
TUAP_DATA = 0x4013
TUAP_CLIENT_STOP = 0x5001
TUAP_SERVER_STOP = 0x5002
TUAP_MAGIC = 0x4366
TUAP_PADDING_TAIL = b"tsf4g"


class UpdateConnection:
    def __init__(self, host, port, service):
        self.socket = socket.create_connection((host, port), timeout=SOCKET_TIMEOUT_SECONDS)
        self.cipher = None
        self.service = service

    def close(self):
        self.socket.close()

    def send(self, command, section, plain=b"", sequence=0):
        body = b""
        if plain:
            padding = (16 if len(plain) % 16 <= 10 else 32) - len(plain) % 16
            padded = plain + os.urandom(padding - 6) + TUAP_PADDING_TAIL + bytes([padding])
            body = self.cipher.encrypt_cbc(padded, TUAP_IV)
        head = tlv(
            (1, 0, TUAP_MAGIC),
            (2, 0, 0),
            (3, 0, 1),
            (4, 0, command),
            (5, 0, 0),
            (6, 1, 0),
            (7, 0, sequence),
            (8, 1, 0),
            (9, 0, len(body)),
        )
        kind = 5 if isinstance(section, bytes) else 1
        self.socket.sendall(struct.pack(">H", TUAP_MAGIC) + frame(tlv((1, 5, head), (2, 5, tlv((command, kind, section))))) + body)

    def receive_exact(self, count):
        result = bytearray()
        while len(result) < count:
            chunk = self.socket.recv(count - len(result))
            if not chunk:
                raise FetchError("The official update server closed the connection")
            result += chunk
        return bytes(result)

    def receive(self, expected_command):
        prefix = self.receive_exact(5)
        size = int.from_bytes(prefix[1:], "big")
        if prefix[0] != 0xAA or not 5 <= size <= 4096:
            raise FetchError("The official update server sent an invalid TUAP header")
        outer = self.receive_exact(size - 5)
        head = read_tlv(nested_tlv(outer, 1))
        if head.get(1) != TUAP_MAGIC or head.get(3) != 1 or 4 not in head:
            raise FetchError("The official update server sent an unknown TUAP version")
        body_size = head.get(9, 0)
        if not 0 <= body_size <= MAX_TUAP_BODY:
            raise FetchError("The official update server sent an oversized TUAP body")
        body = self.receive_exact(body_size)
        command = head[4]
        if command == TUAP_ACK:
            self.cipher = Aes128(nested_tlv(outer, 2, command, 3, 2, 2))
        if body and head.get(6) == 1:
            if self.cipher is None or len(body) % AES_BLOCK_BYTES:
                raise FetchError("The official update server sent an invalid encrypted body")
            body = self.cipher.decrypt_cbc(body, TUAP_IV)
            if body[-6:-1] != TUAP_PADDING_TAIL or not 6 <= body[-1] <= min(21, len(body)):
                raise FetchError("The official update server sent invalid TUAP padding")
            body = body[: -body[-1]]
        if command == TUAP_SERVER_STOP:
            raise FetchError(f"The official update server stopped the session ({body.hex()})")
        if command != expected_command:
            raise FetchError(f"The official update server sent TUAP {command:#x}, expected {expected_command:#x}")
        return head, outer, body

    def authenticate(self):
        self.send(TUAP_SYN, syn_section(self.service))
        head, _, body = self.receive(TUAP_ACK)
        if nested_tlv(unframe(body), TUAP_ACK, 1) != 0:
            raise FetchError("The official update server refused the TUAP handshake")
        server_time = head.get(5)
        if not isinstance(server_time, int):
            raise FetchError("The official update server sent no server time")
        token = hashlib.sha1(f"{GAME_ID}HA{GAME_KEY}HA{server_time}".encode("ascii")).hexdigest().encode("ascii")
        auth = tlv((1, 0, 0), (2, 5, token), (4, 0, server_time), (5, 5, b""), (6, 5, tlv((1, 5, b""), (2, 5, b""))))
        self.send(TUAP_AUTH, 0, frame(tlv((TUAP_AUTH, 5, auth))), 2)
        _, _, body = self.receive(TUAP_AUTH_REPLY)
        if nested_tlv(unframe(body), TUAP_AUTH_REPLY, 2) != 0:
            raise FetchError("The official update server refused TUAP authentication")
        self.receive(TUAP_BINGO)

    def call(self, method, arguments, sequence):
        self.authenticate()
        name = f"{self.service}:{method}".encode("ascii")
        request = struct.pack(">I", len(name)) + name + struct.pack(">Bq", 1, sequence) + thrift_struct((1, 12, arguments))
        section = tlv((1, 1, 0), (2, 1, 0), (3, 1, 4), (4, 5, service_route(self.service)))
        self.send(TUAP_DATA, section, struct.pack(">I", 3) + request, 3)
        _, outer, body = self.receive(TUAP_DATA)
        if nested_tlv(outer, 2, TUAP_DATA, 1) == 1:
            body = decompress_lz4_block(body)
        reader = Reader(body)
        reply_name = reader.take(reader.number(4))
        reply_kind, reply_sequence = reader.number(1), reader.number(8, signed=True)
        values = read_thrift(reader, 12)
        if reader.position != len(body) or reply_name != name or reply_kind != 2 or reply_sequence != sequence or 0 not in values:
            raise FetchError(f"The official update server sent an unexpected reply to {name.decode('ascii')}")
        self.send(TUAP_CLIENT_STOP, 0, frame(tlv((TUAP_CLIENT_STOP, 5, tlv((1, 0, 0))))), 4)
        return values[0]


def update_rpc(host, port, service, method, arguments, sequence):
    connection = UpdateConnection(host, port, service)
    try:
        return connection.call(method, arguments, sequence)
    finally:
        connection.close()


def query_puffer_address():
    address_host = f"puffer.6.{GAME_ID}.nishikigcloud.iegcom.com"
    arguments = thrift_struct((1, 10, GAME_ID), (2, 10, 6), (3, 11, "puffer"))
    result = update_rpc(address_host, ADDRESS_SERVICE_PORT, "QueryAddrSvr", "QueryAddrInfo", arguments, 0)
    addresses = result.get(3)
    if result.get(1) != 0 or not isinstance(addresses, list) or not addresses:
        raise FetchError(f"The official address service refused the Puffer query (result {result.get(1)})")
    first = addresses[0]
    host, port = first.get(1), first.get(2)
    if not isinstance(host, str) or not isinstance(port, int) or not 1 <= port <= 65535:
        raise FetchError("The official address service sent an invalid Puffer address")
    try:
        if not ipaddress.ip_address(host).is_global:
            raise FetchError("The official address service sent a non-public Puffer address")
    except ValueError as error:
        raise FetchError("The official address service sent an invalid Puffer address") from error
    return host, port


def packed_version(text):
    parts = [int(part) for part in text.split(".")]
    return sum(part << (16 * (3 - index)) for index, part in enumerate(parts))


def version_text(value):
    return ".".join(str((value >> shift) & 0xFFFF) for shift in (48, 32, 16, 0))


def puffer_request(platform):
    comm = thrift_struct(
        (1, 8, 1),
        (2, 10, GAME_ID),
        (3, 8, platform.puffer_product_id),
        (4, 11, ""),
        (5, 11, ""),
        (6, 8, 1),
        (7, 8, platform.dolphin_product_id),
        (8, 10, packed_version(CLIENT_APP_VERSION)),
        (9, 10, 0),
    )
    return thrift_struct((1, 12, comm), (2, 11, ""), (4, 12, thrift_struct((1, 3, 0), (2, 3, 0), (3, 3, 1))))


@dataclass(frozen=True)
class ArchiveInfo:
    platform: Platform
    url: str
    md5: str
    version: str
    file_name: str


def archive_file_name(url, platform):
    parsed = urlsplit(url)
    if (
        parsed.scheme not in ("http", "https")
        or parsed.hostname != ARCHIVE_CDN_HOST
        or parsed.port not in (None, 80, 443)
        or parsed.username
        or parsed.password
        or parsed.fragment
        or not parsed.path.startswith(f"/dolphin/{GAME_ID}/{platform.puffer_product_id}/")
    ):
        raise FetchError(f"Puffer sent an archive URL outside the official CDN: {url}")
    file_name = parsed.path.rsplit("/", 1)[-1]
    if not re.fullmatch(rf"{GAME_ID}_{platform.puffer_product_id}_\d+\.\d+\.\d+\.\d+_[A-Za-z0-9_]+\.ifs", file_name):
        raise FetchError(f"Puffer sent an archive URL with an unexpected file name: {url}")
    return file_name


def query_archive(platform, puffer_address):
    result = update_rpc(*puffer_address, "PufferUpdateService", "GetUpdateInfo", puffer_request(platform), 1)
    if result.get(1) != 0 or not isinstance(result.get(3), dict):
        raise FetchError(f"Puffer refused {platform.name} product {platform.puffer_product_id} (result {result.get(1)})")
    comm = result.get(2, {})
    if (
        comm.get(3) != platform.puffer_product_id
        or comm.get(7) != platform.dolphin_product_id
        or comm.get(8) != packed_version(CLIENT_APP_VERSION)
    ):
        raise FetchError(f"Puffer answered a different product or version than {platform.name} {CLIENT_APP_VERSION}")
    update = result[3]
    url = update.get(6) or update.get(1)
    md5 = update.get(2)
    version = update.get(5)
    if not isinstance(url, str) or not isinstance(md5, str) or not MD5_HEX.fullmatch(md5.lower()) or not isinstance(version, int):
        raise FetchError(f"Puffer sent an incomplete archive description for {platform.name}")
    return ArchiveInfo(platform, url, md5.lower(), version_text(version), archive_file_name(url, platform))


class OfficialArchiveRedirect(urllib.request.HTTPRedirectHandler):
    def __init__(self, platform):
        super().__init__()
        self.platform = platform

    def redirect_request(self, request, response, code, message, headers, new_url):
        archive_file_name(new_url, self.platform)
        return super().redirect_request(request, response, code, message, headers, new_url)


def file_md5(path):
    digest = hashlib.md5()
    with path.open("rb") as source:
        while chunk := source.read(HASH_READ_BYTES):
            digest.update(chunk)
    return digest.hexdigest()


def read_json_file(path):
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as error:
        raise FetchError(f"{path} is not valid JSON: {error}") from error


def write_json_file(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def format_gigabytes(byte_count):
    return f"{byte_count / 1e9:.2f} GB"


def fetch_range(opener, info, state, position, size):
    end = position + ARCHIVE_RANGE_BYTES - 1
    if size is not None:
        end = min(end, size - 1)
    headers = {"Range": f"bytes={position}-{end}", "Accept-Encoding": "identity"}
    request = urllib.request.Request(info.url, headers=headers)
    with opener.open(request, timeout=ARCHIVE_TIMEOUT_SECONDS) as response:
        content_range = re.fullmatch(r"bytes (\d+)-(\d+)/(\d+)", response.headers.get("Content-Range", ""))
        if response.status != 206 or not content_range or response.headers.get("Content-Encoding", "identity") != "identity":
            raise FetchError("The official CDN did not answer the byte range request")
        start, last, total = map(int, content_range.groups())
        if start != position or last != min(end, total - 1) or (size is not None and total != size):
            raise FetchError("The official CDN answered a different byte range or archive size")
        validators = {"etag": response.headers.get("ETag"), "last_modified": response.headers.get("Last-Modified")}
        for key, value in validators.items():
            if key in state and state[key] != value:
                raise FetchError("The archive on the official CDN changed during the download; delete the .part file to start again")
        state.update(validators)
        state["size"] = total
        data = bytearray()
        remaining = last - start + 1
        while remaining:
            chunk = response.read(min(ARCHIVE_READ_BYTES, remaining))
            if not chunk:
                raise ConnectionError("The official CDN closed the connection mid-range")
            data += chunk
            remaining -= len(chunk)
        return bytes(data), total


def download_archive(info, resource_directory):
    product_folder = resource_directory / str(info.platform.puffer_product_id)
    product_folder.mkdir(parents=True, exist_ok=True)
    target = product_folder / info.file_name
    others = [path for path in product_folder.glob("*.ifs") if path.name != info.file_name]
    if others:
        names = ", ".join(path.name for path in others)
        raise FetchError(f"{product_folder} already holds {names}; the server serves exactly one .ifs per product, so remove it to download version {info.version}")
    if target.exists():
        print(f"{info.platform.name}: {target.name} exists, checking its MD5", flush=True)
        if file_md5(target) != info.md5:
            raise FetchError(f"{target} does not match the official MD5 {info.md5}; delete it and run again")
        print(f"{info.platform.name}: already downloaded and verified", flush=True)
        return
    partial = product_folder / (info.file_name + ".part")
    state_path = product_folder / (info.file_name + ".download.json")
    state = {"url": info.url, "md5": info.md5}
    if state_path.exists():
        saved = read_json_file(state_path)
        if saved.get("md5") != info.md5:
            raise FetchError(f"{partial} belongs to a different archive; delete it and {state_path.name} to start again")
        state = saved
        state["url"] = info.url
    elif partial.exists():
        raise FetchError(f"{partial} has no {state_path.name}; delete it to start again")
    opener = urllib.request.build_opener(OfficialArchiveRedirect(info.platform))
    size = state.get("size")
    position = partial.stat().st_size if partial.exists() else 0
    if size is not None and position > size:
        raise FetchError(f"{partial} is larger than the archive; delete it to start again")
    started = time.monotonic()
    started_position = position
    failures = 0
    with partial.open("ab") as output:
        while size is None or position < size:
            try:
                data, size = fetch_range(opener, info, state, position, size)
            except (OSError, http.client.HTTPException) as error:
                failures += 1
                if failures >= ARCHIVE_RANGE_ATTEMPTS:
                    raise FetchError(f"Download stopped after {failures} failed attempts ({error}); run the same command to resume") from error
                time.sleep(2**failures)
                continue
            failures = 0
            output.write(data)
            output.flush()
            position += len(data)
            write_json_file(state_path, state)
            elapsed = max(time.monotonic() - started, 0.001)
            rate = (position - started_position) / elapsed / 1e6
            print(f"{info.platform.name}: {format_gigabytes(position)} of {format_gigabytes(size)} ({rate:.1f} MB/s)", flush=True)
    print(f"{info.platform.name}: checking the MD5", flush=True)
    if file_md5(partial) != info.md5:
        raise FetchError(f"{partial} does not match the official MD5 {info.md5}; it was kept, delete it to download again")
    partial.replace(target)
    state_path.unlink()
    print(f"{info.platform.name}: saved {target}", flush=True)


def fetch_archives(platforms, resource_directory):
    print("Asking the official address service for Puffer", flush=True)
    puffer_address = query_puffer_address()
    archives = [query_archive(platform, puffer_address) for platform in platforms]
    for info in archives:
        print(f"{info.platform.name}: version {info.version}, {info.file_name}, md5 {info.md5}", flush=True)
    for info in archives:
        download_archive(info, resource_directory)


@dataclass(frozen=True)
class MirrorFile:
    relative_path: str
    md5: str
    size: int


def load_image_file_list():
    if not IMAGE_FILE_LIST_PATH.is_file():
        raise FetchError(f"{IMAGE_FILE_LIST_PATH} is missing")
    listing = read_json_file(IMAGE_FILE_LIST_PATH)
    index = listing.get("index") if isinstance(listing, dict) else None
    entries = listing.get("files") if isinstance(listing, dict) else None
    if not isinstance(index, dict) or not isinstance(index.get("md5"), str) or not isinstance(entries, list):
        raise FetchError(f"{IMAGE_FILE_LIST_PATH} has an unknown layout")
    files = []
    for entry in entries:
        if (
            not isinstance(entry, list)
            or len(entry) != 3
            or not isinstance(entry[0], str)
            or not IMAGE_RELATIVE_PATH.fullmatch(entry[0])
            or ".." in entry[0]
            or not isinstance(entry[1], str)
            or not MD5_HEX.fullmatch(entry[1])
            or not isinstance(entry[2], int)
            or entry[2] <= 0
        ):
            raise FetchError(f"{IMAGE_FILE_LIST_PATH} has an invalid entry: {entry!r}")
        files.append(MirrorFile(*entry))
    return index["md5"], files


class CdnClient:
    def __init__(self, host, user_agent):
        self.host = host
        self.user_agent = user_agent
        self.connections = threading.local()

    def connection(self):
        if getattr(self.connections, "value", None) is None:
            self.connections.value = http.client.HTTPSConnection(self.host, timeout=IMAGE_TIMEOUT_SECONDS)
        return self.connections.value

    def reset(self):
        connection = getattr(self.connections, "value", None)
        if connection is not None:
            connection.close()
        self.connections.value = None

    def get(self, path):
        for attempt in range(IMAGE_REQUEST_ATTEMPTS):
            try:
                connection = self.connection()
                connection.request("GET", path, headers={"User-Agent": self.user_agent, "Connection": "keep-alive"})
                response = connection.getresponse()
                body = response.read()
                if response.status in (200, 403, 404):
                    return response.status, body
            except (http.client.HTTPException, OSError):
                pass
            self.reset()
            time.sleep(2**attempt)
        return None, b""


def bytes_md5(data):
    return hashlib.md5(data).hexdigest()


def write_atomically(target, data):
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + ".part")
    temporary.write_bytes(data)
    temporary.replace(target)


def fetch_image(client, image_folder, image):
    target = image_folder / image.relative_path
    if target.is_file() and target.stat().st_size == image.size and file_md5(target) == image.md5:
        return "present"
    status, data = client.get(f"{IMAGE_CDN_ROOT}/{image.relative_path}")
    if status in (403, 404):
        return "missing"
    if status != 200:
        return "failed"
    if len(data) != image.size or bytes_md5(data) != image.md5:
        return "changed"
    write_atomically(target, data)
    return "downloaded"


def fetch_images(resource_directory, workers):
    snapshot_index_md5, files = load_image_file_list()
    image_folder = resource_directory / IMAGE_FOLDER_NAME
    client = CdnClient(IMAGE_CDN_HOST, IMAGE_USER_AGENT)
    status, index = client.get(f"{IMAGE_CDN_ROOT}/{IMAGE_INDEX_FILE}")
    if status != 200:
        raise FetchError(f"The official image CDN did not send its index (status {status})")
    write_atomically(image_folder / IMAGE_INDEX_FILE, index)
    if bytes_md5(index) != snapshot_index_md5:
        print("The official image index changed since this file list was made; images that changed on the CDN are reported and skipped", flush=True)
    total_bytes = sum(image.size for image in files)
    print(f"Fetching {len(files)} images ({format_gigabytes(total_bytes)}) into {image_folder}", flush=True)
    outcomes = {}
    with concurrent.futures.ThreadPoolExecutor(workers) as pool:
        for count, (image, outcome) in enumerate(zip(files, pool.map(lambda image: fetch_image(client, image_folder, image), files)), 1):
            outcomes.setdefault(outcome, []).append(image.relative_path)
            if count % IMAGE_PROGRESS_EVERY == 0 or count == len(files):
                summary = ", ".join(f"{name} {len(paths)}" for name, paths in sorted(outcomes.items()))
                print(f"{count} of {len(files)}: {summary}", flush=True)
    for outcome in ("changed", "missing", "failed"):
        for path in outcomes.get(outcome, [])[:10]:
            print(f"{outcome}: {path}", flush=True)
    if outcomes.get("failed"):
        raise FetchError(f"{len(outcomes['failed'])} images failed to download; run the same command to retry them")


def load_gallery_file_list():
    if not GALLERY_FILE_LIST_PATH.is_file():
        raise FetchError(f"{GALLERY_FILE_LIST_PATH} is missing")
    listing = read_json_file(GALLERY_FILE_LIST_PATH)
    entries = listing.get("files") if isinstance(listing, dict) else None
    if not isinstance(entries, list):
        raise FetchError(f"{GALLERY_FILE_LIST_PATH} has an unknown layout")
    files = []
    for entry in entries:
        if (
            not isinstance(entry, list)
            or len(entry) != 3
            or not isinstance(entry[0], str)
            or not GALLERY_RELATIVE_PATH.fullmatch(entry[0])
            or ".." in entry[0]
            or not isinstance(entry[1], str)
            or not MD5_HEX.fullmatch(entry[1])
            or not isinstance(entry[2], int)
            or entry[2] <= 0
        ):
            raise FetchError(f"{GALLERY_FILE_LIST_PATH} has an invalid entry: {entry!r}")
        files.append(MirrorFile(*entry))
    return files


def fetch_gallery_file(client, gallery_folder, file):
    target = gallery_folder / file.relative_path
    if target.is_file() and target.stat().st_size == file.size and file_md5(target) == file.md5:
        return "present"
    status, data = client.get(f"{GALLERY_ROOT}/{file.relative_path}")
    if status in (403, 404):
        return "missing"
    if status != 200:
        return "failed"
    if len(data) != file.size or bytes_md5(data) != file.md5:
        return "changed"
    write_atomically(target, data)
    return "downloaded"


def fetch_gallery(resource_directory, workers):
    files = load_gallery_file_list()
    gallery_folder = resource_directory / GALLERY_FOLDER_NAME
    client = CdnClient(GALLERY_HOST, GALLERY_USER_AGENT)
    total_bytes = sum(file.size for file in files)
    print(f"Fetching {len(files)} gallery files ({format_gigabytes(total_bytes)}) into {gallery_folder}", flush=True)
    outcomes = {}
    with concurrent.futures.ThreadPoolExecutor(workers) as pool:
        for file, outcome in zip(files, pool.map(lambda file: fetch_gallery_file(client, gallery_folder, file), files)):
            outcomes.setdefault(outcome, []).append(file.relative_path)
    print(", ".join(f"{name} {len(paths)}" for name, paths in sorted(outcomes.items())), flush=True)
    for outcome in ("changed", "missing", "failed"):
        for path in outcomes.get(outcome, [])[:10]:
            print(f"{outcome}: {path}", flush=True)
    if outcomes.get("failed"):
        raise FetchError(f"{len(outcomes['failed'])} gallery files failed to download; run the same command to retry them")


def resolve_resource_directory(argument):
    if argument is not None:
        return argument.resolve()
    configured = DEFAULT_RESOURCE_DIRECTORY
    if APPSETTINGS_PATH.is_file():
        settings = read_json_file(APPSETTINGS_PATH)
        if not isinstance(settings, dict):
            raise FetchError(f"{APPSETTINGS_PATH} is not a JSON object")
        section = settings.get("ResourceArchive")
        if isinstance(section, dict) and isinstance(section.get("Directory"), str) and section["Directory"].strip():
            configured = section["Directory"]
    return (REPOSITORY_ROOT / configured).resolve()


def parse_arguments():
    parser = argparse.ArgumentParser(description="Download the official UNITE resource archives, image CDN files and Gallery pages a SpiritUNITE host serves.")
    parser.add_argument(
        "--resources",
        type=Path,
        help="Resource directory; defaults to ResourceArchive.Directory from appsettings.json against the repository root, else resources/",
    )
    commands = parser.add_subparsers(dest="command", required=True)
    archives = commands.add_parser("archives", help="Download the about 9.3 GB resource archive for each platform")
    archives.add_argument("--platform", choices=[*PLATFORMS, "all"], default="all")
    images = commands.add_parser("images", help="Download the about 1.3 GB image CDN mirror")
    images.add_argument("--workers", type=int, default=IMAGE_MAX_WORKERS)
    gallery = commands.add_parser("gallery", help="Download the about 0.4 GB Menu > Gallery manga site")
    gallery.add_argument("--workers", type=int, default=IMAGE_MAX_WORKERS)
    arguments = parser.parse_args()
    if arguments.command in ("images", "gallery") and not 1 <= arguments.workers <= IMAGE_MAX_WORKERS:
        parser.error(f"--workers must be between 1 and {IMAGE_MAX_WORKERS}")
    return arguments


def main():
    arguments = parse_arguments()
    try:
        resource_directory = resolve_resource_directory(arguments.resources)
        print(f"Resource directory: {resource_directory}", flush=True)
        if arguments.command == "archives":
            platforms = list(PLATFORMS.values()) if arguments.platform == "all" else [PLATFORMS[arguments.platform]]
            fetch_archives(platforms, resource_directory)
        elif arguments.command == "gallery":
            fetch_gallery(resource_directory, arguments.workers)
        else:
            fetch_images(resource_directory, arguments.workers)
    except (FetchError, OSError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        print("Stopped; run the same command to resume", file=sys.stderr)
        return 130
    return 0


if __name__ == "__main__":
    sys.exit(main())
