using System.Buffers;
using System.Buffers.Binary;
using SpiritUNITE.Network.Gcp;

namespace SpiritUNITE.Network.Tuap;

public enum TlvType : byte
{
    Varint = 0,
    Byte = 1,
    Bytes = 5,
}

public readonly record struct TlvField(uint Tag, TlvType Type, ulong Number, ReadOnlyMemory<byte> Bytes);

public sealed class TlvFields
{
    public const int MaxFields = 64;

    private const int TypeBits = 4;
    private const int TypeMask = (1 << TypeBits) - 1;
    private const int MaxVarintBytes = 10;
    private const int VarintPayloadBits = 7;
    private const byte VarintContinue = 0x80;

    private readonly List<TlvField> fields;

    private TlvFields(List<TlvField> fields) => this.fields = fields;

    public IReadOnlyList<TlvField> All => fields;

    public static TlvFields Parse(ReadOnlyMemory<byte> data)
    {
        var fields = new List<TlvField>();
        var span = data.Span;
        var position = 0;
        while (position < span.Length)
        {
            if (fields.Count == MaxFields)
            {
                throw new GcpProtocolException($"TLV holds more than {MaxFields} fields");
            }

            var key = ReadVarint(span, ref position);
            if (key > uint.MaxValue)
            {
                throw new GcpProtocolException("TLV key is out of range");
            }

            var tag = (uint)(key >> TypeBits);
            var type = (TlvType)(key & TypeMask);
            switch (type)
            {
                case TlvType.Varint:
                    fields.Add(new TlvField(tag, type, ReadVarint(span, ref position), ReadOnlyMemory<byte>.Empty));
                    break;
                case TlvType.Byte:
                    RequireRemaining(span, position, 1);
                    fields.Add(new TlvField(tag, type, span[position], data.Slice(position, 1)));
                    position++;
                    break;
                case TlvType.Bytes:
                    RequireRemaining(span, position, sizeof(uint));
                    var length = BinaryPrimitives.ReadUInt32BigEndian(span[position..]);
                    position += sizeof(uint);
                    RequireRemaining(span, position, length);
                    fields.Add(new TlvField(tag, type, length, data.Slice(position, (int)length)));
                    position += (int)length;
                    break;
                default:
                    throw new GcpProtocolException($"TLV field {tag} has unknown type {(int)type}");
            }
        }

        return new TlvFields(fields);
    }

    public TlvField? Find(uint tag)
    {
        foreach (var field in fields)
        {
            if (field.Tag == tag)
            {
                return field;
            }
        }

        return null;
    }

    public ulong RequireNumber(uint tag) =>
        Find(tag) is { Type: TlvType.Varint or TlvType.Byte } field
            ? field.Number
            : throw new GcpProtocolException($"TLV field {tag} is missing or not a number");

    public ReadOnlyMemory<byte> RequireBytes(uint tag, int maxLength) =>
        Find(tag) is { Type: TlvType.Bytes } field && field.Bytes.Length <= maxLength
            ? field.Bytes
            : throw new GcpProtocolException($"TLV field {tag} is missing, not bytes or longer than {maxLength}");

    public TlvFields RequireNested(uint tag, int maxLength) => Parse(RequireBytes(tag, maxLength));

    private static ulong ReadVarint(ReadOnlySpan<byte> span, ref int position)
    {
        ulong value = 0;
        for (var index = 0; index < MaxVarintBytes; index++)
        {
            RequireRemaining(span, position, 1);
            var next = span[position++];
            var payload = (ulong)(next & ~VarintContinue);
            var shift = index * VarintPayloadBits;
            if (shift == (MaxVarintBytes - 1) * VarintPayloadBits && payload > 1)
            {
                throw new GcpProtocolException("TLV varint overflows 64 bits");
            }

            value |= payload << shift;
            if ((next & VarintContinue) == 0)
            {
                return value;
            }
        }

        throw new GcpProtocolException("TLV varint is longer than 10 bytes");
    }

    private static void RequireRemaining(ReadOnlySpan<byte> span, int position, long count)
    {
        if (count > span.Length - position)
        {
            throw new GcpProtocolException("TLV field runs past the end of its data");
        }
    }
}

public sealed class TlvWriter
{
    private const int TypeBits = 4;

    private readonly ArrayBufferWriter<byte> buffer = new();

    public ReadOnlySpan<byte> Written => buffer.WrittenSpan;

    public TlvWriter Number(uint tag, ulong value)
    {
        WriteKey(tag, TlvType.Varint);
        WriteVarint(value);
        return this;
    }

    public TlvWriter Byte(uint tag, byte value)
    {
        WriteKey(tag, TlvType.Byte);
        buffer.Write([value]);
        return this;
    }

    public TlvWriter Bytes(uint tag, ReadOnlySpan<byte> value)
    {
        WriteKey(tag, TlvType.Bytes);
        Span<byte> length = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)value.Length);
        buffer.Write(length);
        buffer.Write(value);
        return this;
    }

    public TlvWriter Nested(uint tag, TlvWriter nested) => Bytes(tag, nested.Written);

    public byte[] ToArray() => buffer.WrittenSpan.ToArray();

    private void WriteKey(uint tag, TlvType type) => WriteVarint(((ulong)tag << TypeBits) | (byte)type);

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            buffer.Write([(byte)(value | 0x80)]);
            value >>= 7;
        }

        buffer.Write([(byte)value]);
    }
}
