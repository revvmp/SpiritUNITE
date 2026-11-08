using System.Buffers.Binary;
using Google.Protobuf;
using SpiritUnite.Network.GVoice.Messages;

namespace SpiritUnite.Network.GVoice;

public static class CDNVProtoRtcp
{
    public const uint Magic = 0x20180929;
    public const int MagicSize = sizeof(uint);

    public static bool IsRtcp(ReadOnlySpan<byte> datagram) =>
        datagram.Length >= MagicSize && BinaryPrimitives.ReadUInt32LittleEndian(datagram) == Magic;

    public static bool TryParse(ReadOnlySpan<byte> datagram, out QosMsg message)
    {
        message = null!;
        if (!IsRtcp(datagram))
        {
            return false;
        }

        try
        {
            message = QosMsg.Parser.ParseFrom(datagram[MagicSize..]);
            return true;
        }
        catch (InvalidProtocolBufferException)
        {
            return false;
        }
    }

    public static byte[] Build(QosMsg message)
    {
        var body = message.ToByteArray();
        var datagram = new byte[MagicSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(datagram, Magic);
        body.CopyTo(datagram, MagicSize);
        return datagram;
    }
}
