using System.Buffers.Binary;
using System.Text;

namespace SpiritUNITE.Network.GVoice;

public enum AVCmd : ushort
{
    JoinRoomReq = 1,
    JoinRoomRsp = 2,
    QuitRoomReq = 3,
    QuitRoomRsp = 4,
    GetAuthKeyReq = 13,
    GetAuthKeyRsp = 14,
    GetAuthKeyAndWexinIDReq = 20,
    GetAuthKeyAndWexinIDRsp = 21,
    GetConfigReq = 22,
    GetConfigRsp = 23,
}

public sealed record AVProtoHead(AVCmd Cmd, uint ProtoVer, uint SeqNum, string BusinessId);

public sealed record AVJoinRoomReq(string OpenId, string RoomName, string ClientIp, uint ClientNetType, uint TimeSince1970, int SignatureLength);

public sealed record AVGetConfigReq(string OpenId, uint TimeSince1970, int SignatureLength, string Data);

public sealed class AVProtoMessage
{
    public const ushort Magic = 0x7575;
    public const uint PlaintextProtoVer = 222;
    public const int BusinessIdCapacity = 128;
    public const int OpenIdCapacity = 128;
    public const int RoomNameCapacity = 128;
    public const int ClientIpCapacity = 64;
    public const int SignatureCapacity = 128;
    public const int ErrMsgCapacity = 1024;
    public const int RoomInfoJsonCapacity = 1024;
    public const int MaxConfigDataLength = 8192;

    private const int BodyLengthOffset = 2;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private AVProtoMessage(AVProtoHead head, ReadOnlyMemory<byte> body)
    {
        Head = head;
        Body = body;
    }

    public AVProtoHead Head { get; }
    public ReadOnlyMemory<byte> Body { get; }

    public static bool TryParse(ReadOnlyMemory<byte> datagram, out AVProtoMessage message)
    {
        message = null!;
        var reader = new TdrReader(datagram.Span);
        if (!reader.TryU16(out var magic) || magic != Magic
            || !reader.TryU16(out var bodyLength)
            || !reader.TryU16(out var cmd)
            || !reader.TryU32(out var protoVer)
            || !reader.TryU32(out var seqNum)
            || !reader.TryString(BusinessIdCapacity, out var businessId)
            || datagram.Length - reader.Offset != bodyLength)
        {
            return false;
        }

        message = new AVProtoMessage(new AVProtoHead((AVCmd)cmd, protoVer, seqNum, businessId), datagram[reader.Offset..]);
        return true;
    }

    public bool TryReadJoinRoomReq(out AVJoinRoomReq request)
    {
        request = null!;
        var reader = new TdrReader(Body.Span);
        if (!reader.TryString(OpenIdCapacity, out var openId)
            || !reader.TryString(RoomNameCapacity, out var roomName)
            || !reader.TryString(ClientIpCapacity, out var clientIp)
            || !reader.TryU32(out var netType)
            || !reader.TryU32(out var time)
            || !reader.TryString(SignatureCapacity, out var signature))
        {
            return false;
        }

        request = new AVJoinRoomReq(openId, roomName, clientIp, netType, time, signature.Length);
        return true;
    }

    public bool TryReadGetConfigReq(out AVGetConfigReq request)
    {
        request = null!;
        var reader = new TdrReader(Body.Span);
        if (!reader.TryString(OpenIdCapacity, out var openId)
            || !reader.TryU32(out var time)
            || !reader.TryString(SignatureCapacity, out var signature)
            || !reader.TryString(MaxConfigDataLength, out var data))
        {
            return false;
        }

        request = new AVGetConfigReq(openId, time, signature.Length, data);
        return true;
    }

    public static byte[] BuildJoinRoomRsp(AVProtoHead request, string replyBusinessId, uint errCode, string errMsg, string roomInfoJson) =>
        Build(AVCmd.JoinRoomRsp, request.SeqNum, replyBusinessId, body => body
            .U32(errCode)
            .String(errMsg, ErrMsgCapacity)
            .String(roomInfoJson, RoomInfoJsonCapacity)
            .U32(0));

    public static byte[] BuildGetConfigRsp(AVProtoHead request, string replyBusinessId, uint errCode, string errMsg, uint timeSince1970, string data) =>
        Build(AVCmd.GetConfigRsp, request.SeqNum, replyBusinessId, body => body
            .U32(errCode)
            .String(errMsg, ErrMsgCapacity)
            .U32(timeSince1970)
            .String(data, MaxConfigDataLength));

    private static byte[] Build(AVCmd cmd, uint seqNum, string businessId, Action<TdrWriter> writeBody)
    {
        var writer = new TdrWriter()
            .U16(Magic)
            .U16(0)
            .U16((ushort)cmd)
            .U32(PlaintextProtoVer)
            .U32(seqNum)
            .String(businessId, BusinessIdCapacity);
        var headEnd = writer.Length;
        writeBody(writer);
        var bytes = writer.ToArray();
        var bodyLength = bytes.Length - headEnd;
        if (bodyLength > ushort.MaxValue)
        {
            throw new InvalidOperationException($"AV body of {bodyLength} bytes does not fit wBody_len");
        }

        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(BodyLengthOffset), (ushort)bodyLength);
        return bytes;
    }

    private ref struct TdrReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;

        public int Offset { get; private set; }

        public bool TryU16(out ushort value)
        {
            value = 0;
            if (data.Length - Offset < sizeof(ushort))
            {
                return false;
            }

            value = BinaryPrimitives.ReadUInt16BigEndian(data[Offset..]);
            Offset += sizeof(ushort);
            return true;
        }

        public bool TryU32(out uint value)
        {
            value = 0;
            if (data.Length - Offset < sizeof(uint))
            {
                return false;
            }

            value = BinaryPrimitives.ReadUInt32BigEndian(data[Offset..]);
            Offset += sizeof(uint);
            return true;
        }

        public bool TryString(int capacity, out string value)
        {
            value = string.Empty;
            if (!TryU32(out var length) || length == 0 || length > capacity || length > data.Length - Offset)
            {
                return false;
            }

            var raw = data.Slice(Offset, (int)length);
            if (raw[^1] != 0 || raw[..^1].Contains((byte)0))
            {
                return false;
            }

            try
            {
                value = StrictUtf8.GetString(raw[..^1]);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            Offset += (int)length;
            return true;
        }
    }

    private sealed class TdrWriter
    {
        private readonly List<byte> bytes = [];

        public int Length => bytes.Count;

        public TdrWriter U16(ushort value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
            bytes.AddRange(buffer);
            return this;
        }

        public TdrWriter U32(uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            bytes.AddRange(buffer);
            return this;
        }

        public TdrWriter String(string value, int capacity)
        {
            var raw = Encoding.UTF8.GetBytes(value);
            if (raw.Length + 1 > capacity || raw.Contains((byte)0))
            {
                throw new ArgumentException($"TDR string of {raw.Length} bytes does not fit a {capacity} byte field", nameof(value));
            }

            U32((uint)(raw.Length + 1));
            bytes.AddRange(raw);
            bytes.Add(0);
            return this;
        }

        public byte[] ToArray() => [.. bytes];
    }
}
