using System.Buffers.Binary;

namespace SpiritUNITE.Network.Gcp;

public static class Lz4Block
{
    private const int MinMatch = 4;
    private const int LastLiterals = 5;
    private const int MatchFindLimit = 12;
    private const int MaxOffset = ushort.MaxValue;
    private const int HashLog = 12;
    private const uint HashMultiplier = 2654435761;
    private const int RunMask = 15;
    private const int LengthByteLimit = 255;
    private const int LiteralLengthShift = 4;
    private const int NoCandidate = -1;

    public static byte[] Compress(ReadOnlySpan<byte> source)
    {
        var output = new List<byte>(source.Length + source.Length / LengthByteLimit + 16);
        var table = new int[1 << HashLog];
        Array.Fill(table, NoCandidate);
        var anchor = 0;
        var position = 0;
        var matchStartLimit = source.Length - MatchFindLimit;
        var matchEndLimit = source.Length - LastLiterals;

        while (position < matchStartLimit)
        {
            var sequence = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
            var hash = (int)((sequence * HashMultiplier) >> (32 - HashLog));
            var candidate = table[hash];
            table[hash] = position;
            if (candidate == NoCandidate
                || position - candidate > MaxOffset
                || BinaryPrimitives.ReadUInt32LittleEndian(source[candidate..]) != sequence)
            {
                position++;
                continue;
            }

            var matchLength = MinMatch;
            while (position + matchLength < matchEndLimit && source[candidate + matchLength] == source[position + matchLength])
            {
                matchLength++;
            }

            WriteSequence(output, source[anchor..position], position - candidate, matchLength);
            position += matchLength;
            anchor = position;
        }

        WriteLastLiterals(output, source[anchor..]);
        return [.. output];
    }

    public static byte[] Decompress(ReadOnlySpan<byte> source, int maxOutputLength)
    {
        var output = new List<byte>(Math.Min(maxOutputLength, source.Length * 4));
        var position = 0;
        while (true)
        {
            var token = ReadByte(source, ref position);
            var literalLength = ReadLength(source, ref position, token >> LiteralLengthShift);
            if (literalLength > source.Length - position || output.Count + literalLength > maxOutputLength)
            {
                throw new GcpProtocolException("Compressed data has an invalid literal run");
            }

            foreach (var literal in source.Slice(position, literalLength))
            {
                output.Add(literal);
            }

            position += literalLength;
            if (position == source.Length)
            {
                return [.. output];
            }

            if (source.Length - position < sizeof(ushort))
            {
                throw new GcpProtocolException("Compressed data ended inside a match offset");
            }

            int offset = BinaryPrimitives.ReadUInt16LittleEndian(source[position..]);
            position += sizeof(ushort);
            var matchLength = ReadLength(source, ref position, token & RunMask) + MinMatch;
            if (offset == 0 || offset > output.Count || output.Count + matchLength > maxOutputLength)
            {
                throw new GcpProtocolException("Compressed data has an invalid match");
            }

            var matchStart = output.Count - offset;
            for (var index = 0; index < matchLength; index++)
            {
                output.Add(output[matchStart + index]);
            }
        }
    }

    private static void WriteSequence(List<byte> output, ReadOnlySpan<byte> literals, int offset, int matchLength)
    {
        var extraMatchLength = matchLength - MinMatch;
        output.Add((byte)((Math.Min(literals.Length, RunMask) << LiteralLengthShift) | Math.Min(extraMatchLength, RunMask)));
        WriteExtraLength(output, literals.Length);
        output.AddRange(literals);
        output.Add((byte)offset);
        output.Add((byte)(offset >> 8));
        WriteExtraLength(output, extraMatchLength);
    }

    private static void WriteLastLiterals(List<byte> output, ReadOnlySpan<byte> literals)
    {
        output.Add((byte)(Math.Min(literals.Length, RunMask) << LiteralLengthShift));
        WriteExtraLength(output, literals.Length);
        output.AddRange(literals);
    }

    private static void WriteExtraLength(List<byte> output, int length)
    {
        if (length < RunMask)
        {
            return;
        }

        var remaining = length - RunMask;
        while (remaining >= LengthByteLimit)
        {
            output.Add(LengthByteLimit);
            remaining -= LengthByteLimit;
        }

        output.Add((byte)remaining);
    }

    private static int ReadLength(ReadOnlySpan<byte> source, ref int position, int initialLength)
    {
        var length = initialLength;
        if (initialLength != RunMask)
        {
            return length;
        }

        byte next;
        do
        {
            next = ReadByte(source, ref position);
            length += next;
            if (length > GcpHead.MaxPacketSize)
            {
                throw new GcpProtocolException("Compressed data declares a run longer than a packet");
            }
        }
        while (next == LengthByteLimit);

        return length;
    }

    private static byte ReadByte(ReadOnlySpan<byte> source, ref int position) =>
        position < source.Length ? source[position++] : throw new GcpProtocolException("Compressed data ended early");
}
