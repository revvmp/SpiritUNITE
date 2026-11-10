using System.Buffers.Binary;
using System.Text;
using Google.Protobuf;

namespace SpiritUnite.Databin;

public sealed class PbcTable
{
    private const int HeaderSize = 12;
    private const int RowCountOffset = 8;
    private const int IndexEntrySize = sizeof(ulong) + sizeof(uint);
    private const byte FlatVersion = 1;
    private const byte IndexedVersion = 2;
    private const int VarintGroupBits = 7;
    private const byte VarintContinuation = 0x80;

    private static readonly byte[] Magic = "pbc"u8.ToArray();

    private readonly byte[] header;
    private readonly byte[] secondaryIndexes;
    private readonly List<(ulong Key, byte[] Row)> rows;

    private PbcTable(byte[] header, byte[] secondaryIndexes, List<(ulong Key, byte[] Row)> rows)
    {
        this.header = header;
        this.secondaryIndexes = secondaryIndexes;
        this.rows = rows;
    }

    public IReadOnlyList<ulong> Keys => rows.ConvertAll(row => row.Key);

    public static PbcTable Parse(byte[] plain)
    {
        if (plain.Length < HeaderSize || !plain.AsSpan(0, Magic.Length).SequenceEqual(Magic) || plain[3] is not (FlatVersion or IndexedVersion))
        {
            throw new InvalidDataException("Not a version 1 or 2 pbc table");
        }

        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(RowCountOffset)));
        var indexEnd = checked(HeaderSize + count * IndexEntrySize);
        var keys = new ulong[count];
        var starts = new int[count];
        for (var row = 0; row < count; row++)
        {
            var entry = plain.AsSpan(HeaderSize + row * IndexEntrySize);
            keys[row] = BinaryPrimitives.ReadUInt64LittleEndian(entry);
            starts[row] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[sizeof(ulong)..]));
        }

        var rowBase = plain[3] == IndexedVersion ? SecondaryIndexEnd(plain, indexEnd) : indexEnd;
        var rowBytes = plain.Length - rowBase;
        var parsed = new List<(ulong, byte[])>(count);
        for (var row = 0; row < count; row++)
        {
            var end = row + 1 < count ? starts[row + 1] : rowBytes;
            if (starts[row] < 0 || end < starts[row] || end > rowBytes)
            {
                throw new InvalidDataException($"pbc row {keys[row]} lies outside the table");
            }

            parsed.Add((keys[row], plain[(rowBase + starts[row])..(rowBase + end)]));
        }

        return new PbcTable(plain[..HeaderSize], plain[indexEnd..rowBase], parsed);
    }

    public int ReplaceString(ulong key, int fieldNumber, string value)
    {
        var replaced = 0;
        for (var row = 0; row < rows.Count; row++)
        {
            if (rows[row].Key == key)
            {
                var (updated, count) = ReplaceStringField(rows[row].Row, fieldNumber, value);
                rows[row] = (key, updated);
                replaced += count;
            }
        }

        return replaced;
    }

    public byte[] ToArray()
    {
        using var output = new MemoryStream();
        output.Write(header);
        var offset = 0u;
        Span<byte> entry = stackalloc byte[IndexEntrySize];
        foreach (var (key, row) in rows)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(entry, key);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[sizeof(ulong)..], offset);
            output.Write(entry);
            offset = checked(offset + (uint)row.Length);
        }

        output.Write(secondaryIndexes);
        foreach (var (_, row) in rows)
        {
            output.Write(row);
        }

        return output.ToArray();
    }

    private static int SecondaryIndexEnd(byte[] plain, int offset)
    {
        var span = plain.AsSpan();
        var indexCount = ReadCount(span, ref offset);
        for (var index = 0; index < indexCount; index++)
        {
            offset = checked(offset + (int)ReadCount(span, ref offset));
            var valueCount = ReadCount(span, ref offset);
            for (var value = 0; value < valueCount; value++)
            {
                offset = checked(offset + sizeof(int));
                var keyCount = ReadCount(span, ref offset);
                offset = checked(offset + (int)keyCount * sizeof(ulong));
            }
        }

        if (offset > plain.Length)
        {
            throw new InvalidDataException("pbc secondary indexes run past the table");
        }

        return offset;
    }

    private static uint ReadCount(ReadOnlySpan<byte> data, ref int offset)
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
        offset += sizeof(uint);
        return value;
    }

    private static (byte[] Row, int Replaced) ReplaceStringField(byte[] row, int fieldNumber, string value)
    {
        var wantedTag = WireFormat.MakeTag(fieldNumber, WireFormat.WireType.LengthDelimited);
        using var output = new MemoryStream(row.Length + value.Length);
        var replaced = 0;
        var offset = 0;
        while (offset < row.Length)
        {
            var fieldStart = offset;
            var tag = (uint)ReadVarint(row, ref offset);
            var tagEnd = offset;
            SkipValue(row, ref offset, WireFormat.GetTagWireType(tag));
            if (tag != wantedTag)
            {
                output.Write(row, fieldStart, offset - fieldStart);
                continue;
            }

            var encoded = Encoding.UTF8.GetBytes(value);
            output.Write(row, fieldStart, tagEnd - fieldStart);
            WriteVarint(output, (ulong)encoded.Length);
            output.Write(encoded);
            replaced++;
        }

        return (output.ToArray(), replaced);
    }

    private static void SkipValue(byte[] row, ref int offset, WireFormat.WireType wireType)
    {
        switch (wireType)
        {
            case WireFormat.WireType.Varint:
                ReadVarint(row, ref offset);
                break;
            case WireFormat.WireType.Fixed64:
                offset += sizeof(ulong);
                break;
            case WireFormat.WireType.Fixed32:
                offset += sizeof(uint);
                break;
            case WireFormat.WireType.LengthDelimited:
                var length = checked((int)ReadVarint(row, ref offset));
                offset = checked(offset + length);
                break;
            default:
                throw new InvalidDataException($"pbc row uses unsupported wire type {wireType}");
        }

        if (offset > row.Length)
        {
            throw new InvalidDataException("pbc row field runs past the row");
        }
    }

    private static ulong ReadVarint(byte[] data, ref int offset)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += VarintGroupBits)
        {
            if (offset >= data.Length)
            {
                throw new InvalidDataException("Truncated varint in a pbc row");
            }

            var current = data[offset++];
            value |= (ulong)(current & ~VarintContinuation) << shift;
            if (current < VarintContinuation)
            {
                return value;
            }
        }

        throw new InvalidDataException("Overlong varint in a pbc row");
    }

    private static void WriteVarint(Stream output, ulong value)
    {
        while (value >= VarintContinuation)
        {
            output.WriteByte((byte)(value | VarintContinuation));
            value >>= VarintGroupBits;
        }

        output.WriteByte((byte)value);
    }
}
