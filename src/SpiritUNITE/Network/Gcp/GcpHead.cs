using System.Buffers.Binary;

namespace SpiritUNITE.Network.Gcp;

public enum GcpCommand : ushort
{
    Syn = 0x1001,
    Ack = 0x1002,
    AuthReq = 0x2001,
    AuthRsp = 0x2002,
    Data = 0x4013,
    Bingo = 0x6002,
}

public enum KeyMaking : byte
{
    None = 0,
    Auth = 1,
    Server = 2,
    RawDH = 3,
    EncDH = 4,
}

public sealed class GcpProtocolException(string message) : Exception(message);

public readonly record struct GcpHead(
    ushort PackingVersion,
    ushort HeaderVersion,
    ushort Command,
    bool IsEncrypted,
    uint Sequence,
    uint HeadLength,
    uint BodyLength)
{
    public const int Size = 21;
    public const int MaxPacketSize = 64 * 1024;

    private const ushort Magic = 0x3366;
    private const ushort MaxPackingVersion = 11;

    public int PacketLength => (int)(HeadLength + BodyLength);

    public static bool TryParse(ReadOnlySpan<byte> data, out GcpHead head)
    {
        head = default;
        if (data.Length < Size)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(data) != Magic)
        {
            throw new GcpProtocolException("Packet does not start with the GCP magic");
        }

        head = new GcpHead(
            BinaryPrimitives.ReadUInt16BigEndian(data[2..]),
            BinaryPrimitives.ReadUInt16BigEndian(data[4..]),
            BinaryPrimitives.ReadUInt16BigEndian(data[6..]),
            data[8] != 0,
            BinaryPrimitives.ReadUInt32BigEndian(data[9..]),
            BinaryPrimitives.ReadUInt32BigEndian(data[13..]),
            BinaryPrimitives.ReadUInt32BigEndian(data[17..]));
        head.Validate();
        return true;
    }

    public void WriteTo(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, Magic);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], PackingVersion);
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..], HeaderVersion);
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..], Command);
        destination[8] = IsEncrypted ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt32BigEndian(destination[9..], Sequence);
        BinaryPrimitives.WriteUInt32BigEndian(destination[13..], HeadLength);
        BinaryPrimitives.WriteUInt32BigEndian(destination[17..], BodyLength);
    }

    private void Validate()
    {
        if (PackingVersion is 0 or > MaxPackingVersion)
        {
            throw new GcpProtocolException($"Unsupported packing version {PackingVersion}");
        }

        if (HeadLength < Size || (ulong)HeadLength + BodyLength > MaxPacketSize)
        {
            throw new GcpProtocolException($"Invalid packet lengths: head {HeadLength}, body {BodyLength}");
        }
    }
}
