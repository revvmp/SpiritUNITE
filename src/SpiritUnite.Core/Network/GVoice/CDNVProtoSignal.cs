using System.Buffers.Binary;
using Google.Protobuf;
using SpiritUnite.Network.GVoice.Messages;

namespace SpiritUnite.Network.GVoice;

public enum VOIP_CMD : uint
{
    VOIP_CMD_CHECKIN_REQ = 1,
    VOIP_CMD_CHECKIN_RSP = 2,
    VOIP_CMD_CHECKIN_READY = 3,
    VOIP_CMD_STAT_REQ = 4,
    VOIP_CMD_STAT_RSP = 5,
    VOIP_CMD_VERIFY_REQ = 6,
    VOIP_CMD_VERIFY_RSP = 7,
    VOIP_CMD_AUDIO_OPEN_REQ = 8,
    VOIP_CMD_AUDIO_OPEN_RSP = 9,
    VOIP_CMD_AUDIO_CLOSE_REQ = 10,
    VOIP_CMD_AUDIO_CLOSE_RSP = 11,
    VOIP_CMD_EXIT_REQ = 12,
    VOIP_CMD_COORDINATE_REQ = 100,
    VOIP_CMD_COORDINATE_RSP = 101,
    VOIP_CMD_SYNCHRONIZE_MEMINFO = 200,
}

public sealed class CDNVProtoSignal
{
    public const ushort Magic = 0x2828;
    public const int PrefixSize = 5;
    public const uint DaVoipProtoType = 1;

    private CDNVProtoSignal(VoipClient2AccessHead head, ReadOnlyMemory<byte> encryptedBody)
    {
        Head = head;
        EncryptedBody = encryptedBody;
    }

    public VoipClient2AccessHead Head { get; }
    public ReadOnlyMemory<byte> EncryptedBody { get; }
    public VOIP_CMD Cmd => (VOIP_CMD)Head.Cmd;

    public static bool TryParse(ReadOnlyMemory<byte> datagram, out CDNVProtoSignal signal, out string refusal)
    {
        signal = null!;
        var span = datagram.Span;
        if (span.Length < PrefixSize || BinaryPrimitives.ReadUInt16BigEndian(span) != Magic)
        {
            refusal = "short or wrong magic";
            return false;
        }

        var headLength = BinaryPrimitives.ReadUInt16BigEndian(span[3..]);
        if (headLength == 0 || headLength > span.Length - PrefixSize)
        {
            refusal = $"head length {headLength} does not fit {span.Length} bytes";
            return false;
        }

        if (CDNVCipher.Crc8(span[PrefixSize..]) != span[2])
        {
            refusal = "bad CRC-8";
            return false;
        }

        VoipClient2AccessHead head;
        try
        {
            head = VoipClient2AccessHead.Parser.ParseFrom(datagram.Slice(PrefixSize, headLength).Span);
        }
        catch (InvalidProtocolBufferException)
        {
            refusal = "head is not a VoipClient2AccessHead";
            return false;
        }

        if (!head.HasCmd || !head.HasRoomId || !head.HasMemberId || head.ProtoType != DaVoipProtoType)
        {
            refusal = "head misses cmd, room_id or member_id, or is not the da.voip variant";
            return false;
        }

        signal = new CDNVProtoSignal(head, datagram[(PrefixSize + headLength)..]);
        refusal = string.Empty;
        return true;
    }

    public bool TryReadBody<T>(ulong roomKey, MessageParser<T> parser, out T body) where T : IMessage<T>
    {
        body = default!;
        try
        {
            body = parser.ParseFrom(CDNVCipher.RC4(roomKey, EncryptedBody.Span));
            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
    }

    public static byte[] Build(VoipClient2AccessHead head, IMessage body, ulong roomKey)
    {
        var headBytes = head.ToByteArray();
        var encryptedBody = CDNVCipher.RC4(roomKey, body.ToByteArray());
        var datagram = new byte[PrefixSize + headBytes.Length + encryptedBody.Length];
        BinaryPrimitives.WriteUInt16BigEndian(datagram, Magic);
        BinaryPrimitives.WriteUInt16BigEndian(datagram.AsSpan(3), checked((ushort)headBytes.Length));
        headBytes.CopyTo(datagram, PrefixSize);
        encryptedBody.CopyTo(datagram, PrefixSize + headBytes.Length);
        datagram[2] = CDNVCipher.Crc8(datagram.AsSpan(PrefixSize));
        return datagram;
    }
}
