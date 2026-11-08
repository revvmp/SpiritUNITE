using System.Buffers.Binary;

namespace SpiritUNITE.Network.GVoice;

public readonly record struct CDNVDataHead(ulong RoomId, ushort MemberId, uint Sequence, int VoiceLength);

public static class CDNVProtoData
{
    public const ushort Magic = 0x2929;
    public const int DataHeadSize = 16;

    private const byte DataVersion = 1;
    private const int VersionOffset = CDNVProtoSignal.PrefixSize;
    private const int RoomIdOffset = CDNVProtoSignal.PrefixSize + 2;
    private const int MemberIdOffset = RoomIdOffset + sizeof(ulong);
    private const int SequenceOffset = MemberIdOffset + sizeof(ushort);
    private const int CrcCoveredLength = sizeof(ulong) + sizeof(ushort);

    public static bool TryParse(ReadOnlySpan<byte> datagram, out CDNVDataHead head, out string refusal)
    {
        head = default;
        if (datagram.Length < CDNVProtoSignal.PrefixSize + DataHeadSize || BinaryPrimitives.ReadUInt16BigEndian(datagram) != Magic)
        {
            refusal = $"media datagram of {datagram.Length} bytes is shorter than its head";
            return false;
        }

        var headLength = BinaryPrimitives.ReadUInt16BigEndian(datagram[3..]);
        if (headLength < DataHeadSize || (headLength - DataHeadSize) % sizeof(ushort) != 0 || headLength > datagram.Length - CDNVProtoSignal.PrefixSize)
        {
            refusal = $"media head length {headLength} does not fit {datagram.Length} bytes";
            return false;
        }

        if (datagram[VersionOffset] != DataVersion)
        {
            refusal = $"media head version {datagram[VersionOffset]}";
            return false;
        }

        if (CDNVCipher.Crc8(datagram.Slice(RoomIdOffset, CrcCoveredLength)) != datagram[2])
        {
            refusal = "bad media CRC-8";
            return false;
        }

        head = new CDNVDataHead(
            BinaryPrimitives.ReadUInt64BigEndian(datagram[RoomIdOffset..]),
            BinaryPrimitives.ReadUInt16BigEndian(datagram[MemberIdOffset..]),
            BinaryPrimitives.ReadUInt32BigEndian(datagram[SequenceOffset..]),
            datagram.Length - CDNVProtoSignal.PrefixSize - headLength);
        refusal = string.Empty;
        return true;
    }
}
