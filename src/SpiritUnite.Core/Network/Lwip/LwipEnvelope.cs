using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Numerics;

namespace SpiritUnite.Network.Lwip;

[Flags]
public enum LwipFlags : byte
{
    None = 0,
    Fin = 0x01,
    Syn = 0x02,
    Rst = 0x04,
    Psh = 0x08,
    Ack = 0x10,
    CompressedUnreliable = 0x20,
    Unreliable = 0x40,
    AlternativeSocket = 0x80,
}

public readonly record struct ZsdId(ulong Head, ushort Tail);

public readonly record struct ZsdPrefix(ZsdId Id, uint Tag, uint Counter);

public readonly record struct LwipSegment(
    ushort SourcePort,
    ushort DestinationPort,
    uint Seq,
    uint Ack,
    LwipFlags Flags,
    ushort Window,
    ushort TimestampEcho,
    ushort TimestampClock,
    ReadOnlyMemory<byte> Options,
    ReadOnlyMemory<byte> Payload)
{
    public bool Has(LwipFlags flag) => (Flags & flag) == flag;
}

public static class LwipEnvelope
{
    public const int ZsdCodeSize = 30;
    public const int ZsdPrefixSize = 20;
    public const int TcpHeaderSize = 20;
    public const int TrailerSize = 4;
    public const int MaxSegmentSize = 500;
    public const ushort Window = ushort.MaxValue;

    private const int ZsdIdHeadSize = 8;
    private const int ZsdMarkerOffset = 8;
    private const ushort ZsdMarker = 0x0001;
    private const int ZsdIdTailOffset = 10;
    private const int ZsdTagOffset = 12;
    private const int ZsdCounterOffset = 16;
    private const int ZsdChainStart = 10;
    private const int ZsdChainEnd = 26;
    private const int ZsdMixOffset = 26;
    private const uint ZsdChainSeed = 0xFFFFFFFF;
    private const int Ipv4AddressSize = 4;
    private const int Ipv6AddressSize = 16;
    private const int DestinationPortOffset = 2;
    private const int SeqOffset = 4;
    private const int AckOffset = 8;
    private const int DataOffsetOffset = 12;
    private const int FlagsOffset = 13;
    private const int WindowOffset = 14;
    private const int TimestampEchoOffset = 16;
    private const int TimestampClockOffset = 18;
    private const int DataOffsetShift = 4;
    private const int DataOffsetWordSize = 4;
    private const uint HashMultiplier = 33;
    private const uint TrailerMask = 0x7FFFFFFF;

    public static uint Hash(ReadOnlySpan<byte> segment)
    {
        var hash = 0u;
        foreach (var value in segment)
        {
            hash = hash * HashMultiplier + value;
        }

        return hash & TrailerMask;
    }

    public static ZsdId ZsdIdOf(ReadOnlySpan<byte> zsdCode) =>
        new(BinaryPrimitives.ReadUInt64BigEndian(zsdCode), BinaryPrimitives.ReadUInt16BigEndian(zsdCode[ZsdIdHeadSize..]));

    public static uint ZsdChain(ReadOnlySpan<byte> zsdCode)
    {
        var chain = ZsdChainSeed;
        foreach (var value in zsdCode[ZsdChainStart..ZsdChainEnd])
        {
            chain = BitOperations.Crc32C(chain, value);
        }

        return chain;
    }

    public static uint ZsdCodeMix(ReadOnlySpan<byte> zsdCode) => BinaryPrimitives.ReadUInt32LittleEndian(zsdCode[ZsdMixOffset..]);

    public static uint ZsdRelayMix(IPAddress relayAddress, int relayPort)
    {
        Span<byte> address = stackalloc byte[Ipv6AddressSize];
        if (!relayAddress.TryWriteBytes(address, out var written) || written < Ipv4AddressSize)
        {
            throw new ArgumentException("Relay address cannot be written", nameof(relayAddress));
        }

        var addressBytes = relayAddress.AddressFamily == AddressFamily.InterNetworkV6
            ? address[(written - Ipv4AddressSize)..written]
            : address[..Ipv4AddressSize];
        Span<byte> port = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(port, (ushort)relayPort);
        return BinaryPrimitives.ReadUInt32LittleEndian(addressBytes) ^ BinaryPrimitives.ReadUInt16LittleEndian(port);
    }

    public static uint ZsdTag(uint chain, uint mix, uint counter) => BitOperations.Crc32C(chain, counter ^ mix);

    public static bool CounterAfter(uint counter, uint reference) => (int)(counter - reference) > 0;

    public static bool TryReadZsdPrefix(ReadOnlySpan<byte> datagram, out ZsdPrefix prefix)
    {
        prefix = default;
        if (datagram.Length < ZsdPrefixSize || BinaryPrimitives.ReadUInt16BigEndian(datagram[ZsdMarkerOffset..]) != ZsdMarker)
        {
            return false;
        }

        prefix = new ZsdPrefix(
            new ZsdId(BinaryPrimitives.ReadUInt64BigEndian(datagram), BinaryPrimitives.ReadUInt16BigEndian(datagram[ZsdIdTailOffset..])),
            BinaryPrimitives.ReadUInt32LittleEndian(datagram[ZsdTagOffset..]),
            BinaryPrimitives.ReadUInt32LittleEndian(datagram[ZsdCounterOffset..]));
        return true;
    }

    public static void WriteZsdPrefix(Span<byte> destination, ReadOnlySpan<byte> zsdCode, uint tag, uint counter)
    {
        zsdCode[..ZsdIdHeadSize].CopyTo(destination);
        BinaryPrimitives.WriteUInt16BigEndian(destination[ZsdMarkerOffset..], ZsdMarker);
        zsdCode[ZsdIdHeadSize..ZsdIdTailOffset].CopyTo(destination[ZsdIdTailOffset..]);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[ZsdTagOffset..], tag);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[ZsdCounterOffset..], counter);
    }

    public static bool TryParseSegment(ReadOnlyMemory<byte> envelope, out LwipSegment segment)
    {
        segment = default;
        var span = envelope.Span;
        if (span.Length < TcpHeaderSize + TrailerSize)
        {
            return false;
        }

        var covered = span[..^TrailerSize];
        if (BinaryPrimitives.ReadUInt32LittleEndian(span[^TrailerSize..]) != Hash(covered))
        {
            return false;
        }

        var headerLength = (span[DataOffsetOffset] >> DataOffsetShift) * DataOffsetWordSize;
        if (headerLength < TcpHeaderSize || headerLength > covered.Length)
        {
            return false;
        }

        segment = new LwipSegment(
            BinaryPrimitives.ReadUInt16BigEndian(span),
            BinaryPrimitives.ReadUInt16BigEndian(span[DestinationPortOffset..]),
            BinaryPrimitives.ReadUInt32BigEndian(span[SeqOffset..]),
            BinaryPrimitives.ReadUInt32BigEndian(span[AckOffset..]),
            (LwipFlags)span[FlagsOffset],
            BinaryPrimitives.ReadUInt16BigEndian(span[WindowOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(span[TimestampEchoOffset..]),
            BinaryPrimitives.ReadUInt16LittleEndian(span[TimestampClockOffset..]),
            envelope[TcpHeaderSize..headerLength],
            envelope[headerLength..covered.Length]);
        return true;
    }

    public static byte[] BuildSegment(LwipSegment segment)
    {
        var headerLength = TcpHeaderSize + segment.Options.Length;
        var datagram = new byte[headerLength + segment.Payload.Length + TrailerSize];
        WriteSegment(datagram, segment, headerLength);
        return datagram;
    }

    public static void WriteSegment(Span<byte> destination, LwipSegment segment, int headerLength)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination, segment.SourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(destination[DestinationPortOffset..], segment.DestinationPort);
        BinaryPrimitives.WriteUInt32BigEndian(destination[SeqOffset..], segment.Seq);
        BinaryPrimitives.WriteUInt32BigEndian(destination[AckOffset..], segment.Ack);
        destination[DataOffsetOffset] = (byte)(headerLength / DataOffsetWordSize << DataOffsetShift);
        destination[FlagsOffset] = (byte)segment.Flags;
        BinaryPrimitives.WriteUInt16BigEndian(destination[WindowOffset..], segment.Window);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[TimestampEchoOffset..], segment.TimestampEcho);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[TimestampClockOffset..], segment.TimestampClock);
        segment.Options.Span.CopyTo(destination[TcpHeaderSize..]);
        segment.Payload.Span.CopyTo(destination[headerLength..]);
        var coveredLength = headerLength + segment.Payload.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[coveredLength..], Hash(destination[..coveredLength]));
    }
}
