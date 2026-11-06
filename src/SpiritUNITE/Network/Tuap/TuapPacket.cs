using System.Buffers.Binary;
using SpiritUNITE.Network.Gcp;

namespace SpiritUNITE.Network.Tuap;

public readonly record struct TuapHead(GcpCommand Command, ulong Sequence, TlvField Section, int PacketLength, int HeadLength);

public static class TuapPacket
{
    public const int MaxHeadLength = 4096;
    public const int MaxBodyLength = 64 * 1024;
    public const int MaxPacketLength = ClientPrefixLength + MaxHeadLength + MaxBodyLength;

    private const byte FrameMarker = 0xAA;
    private const int FrameHeaderLength = 1 + sizeof(uint);
    private const int ClientPrefixLength = 2;
    private const ushort Magic = 0x4366;
    private const ushort Version = 1;
    private const byte EncryptedBody = 1;
    private const uint HeadTag = 1;
    private const uint SectionTag = 2;
    private const uint MagicTag = 1;
    private const uint ReservedTag = 2;
    private const uint VersionTag = 3;
    private const uint CommandTag = 4;
    private const uint ServerTimeTag = 5;
    private const uint EncryptedTag = 6;
    private const uint SequenceTag = 7;
    private const uint RouteTag = 8;
    private const uint BodyLengthTag = 9;

    private static ReadOnlySpan<byte> ClientPrefix => [0x43, 0x66];

    public static bool TryParseClient(ReadOnlyMemory<byte> buffer, out TuapHead head)
    {
        head = default;
        var span = buffer.Span;
        if (span.Length < ClientPrefixLength + FrameHeaderLength)
        {
            return false;
        }

        if (!span[..ClientPrefixLength].SequenceEqual(ClientPrefix) || span[ClientPrefixLength] != FrameMarker)
        {
            throw new GcpProtocolException($"TUAP packet starts with {Convert.ToHexString(span[..(ClientPrefixLength + 1)])}");
        }

        var frameLength = BinaryPrimitives.ReadUInt32BigEndian(span[(ClientPrefixLength + 1)..]);
        if (frameLength is < FrameHeaderLength or > MaxHeadLength)
        {
            throw new GcpProtocolException($"TUAP head length {frameLength} is out of range");
        }

        var headLength = ClientPrefixLength + (int)frameLength;
        if (span.Length < headLength)
        {
            return false;
        }

        var outer = TlvFields.Parse(buffer[(ClientPrefixLength + FrameHeaderLength)..headLength]);
        var fields = outer.RequireNested(HeadTag, MaxHeadLength);
        if (fields.RequireNumber(MagicTag) != Magic)
        {
            throw new GcpProtocolException("TUAP head has the wrong magic");
        }

        var command = fields.RequireNumber(CommandTag);
        if (command > ushort.MaxValue)
        {
            throw new GcpProtocolException($"TUAP command {command} is out of range");
        }

        var bodyLength = fields.RequireNumber(BodyLengthTag);
        if (bodyLength > MaxBodyLength)
        {
            throw new GcpProtocolException($"TUAP body length {bodyLength} exceeds {MaxBodyLength}");
        }

        var section = TlvFields.Parse(outer.RequireBytes(SectionTag, MaxHeadLength)).Find((uint)command)
            ?? throw new GcpProtocolException($"TUAP command section {command:X4} is missing");

        head = new TuapHead((GcpCommand)command, fields.RequireNumber(SequenceTag), section, headLength + (int)bodyLength, headLength);
        return true;
    }

    public static byte[] BuildServer(GcpCommand command, long serverTime, uint sequence, TlvWriter section, ReadOnlySpan<byte> body)
    {
        var head = new TlvWriter()
            .Number(MagicTag, Magic)
            .Number(ReservedTag, 0)
            .Number(VersionTag, Version)
            .Number(CommandTag, (ushort)command)
            .Number(ServerTimeTag, (ulong)serverTime)
            .Byte(EncryptedTag, EncryptedBody)
            .Number(SequenceTag, sequence)
            .Number(RouteTag, 0)
            .Number(BodyLengthTag, (ulong)body.Length);
        var frame = Frame(new TlvWriter().Nested(HeadTag, head).Nested(SectionTag, section).Written);
        return [.. frame, .. body];
    }

    public static byte[] Frame(ReadOnlySpan<byte> tlv)
    {
        var frame = new byte[FrameHeaderLength + tlv.Length];
        frame[0] = FrameMarker;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), (uint)frame.Length);
        tlv.CopyTo(frame.AsSpan(FrameHeaderLength));
        return frame;
    }

    public static TlvFields Unframe(ReadOnlyMemory<byte> frame)
    {
        var span = frame.Span;
        if (span.Length < FrameHeaderLength
            || span[0] != FrameMarker
            || BinaryPrimitives.ReadUInt32BigEndian(span[1..]) != span.Length)
        {
            throw new GcpProtocolException("TUAP body frame has a bad marker or length");
        }

        return TlvFields.Parse(frame[FrameHeaderLength..]);
    }
}
