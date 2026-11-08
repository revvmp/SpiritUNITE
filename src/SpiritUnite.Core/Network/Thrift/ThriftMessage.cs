using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace SpiritUnite.Network.Thrift;

public enum ThriftMessageType : byte
{
    Call = 1,
    Reply = 2,
}

public sealed record ThriftMessage(string Name, ThriftMessageType Type, long SequenceId, ThriftStruct Body)
{
    public const short ArgumentsFieldId = 1;
    public const short ResultFieldId = 0;
    public const int MaxNameLength = 128;

    public ThriftStruct? Arguments => Body.FindStruct(ArgumentsFieldId);

    public static ThriftMessage Read(ReadOnlySpan<byte> data)
    {
        var reader = new ThriftReader(data);
        var message = reader.ReadMessage();
        if (!reader.AtEnd)
        {
            throw new ThriftProtocolException($"Thrift message has {reader.Remaining} trailing bytes");
        }

        return message;
    }

    public ThriftMessage ReplyWith(ThriftStruct result) =>
        new(Name, ThriftMessageType.Reply, SequenceId, new ThriftStruct([new ThriftField(ResultFieldId, result)]));

    public byte[] Write()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var name = Encoding.UTF8.GetBytes(Name);
        ThriftWriter.WriteI32(buffer, name.Length);
        buffer.Write(name);
        buffer.Write([(byte)Type]);
        ThriftWriter.WriteI64(buffer, SequenceId);
        ThriftWriter.WriteStructFields(buffer, Body, depth: 1);
        return buffer.WrittenSpan.ToArray();
    }
}

internal static class ThriftWriter
{
    public static void WriteValue(ArrayBufferWriter<byte> buffer, ThriftValue value, int depth)
    {
        if (depth > ThriftReader.MaxDepth)
        {
            throw new ArgumentException("Thrift value nests too deeply", nameof(value));
        }

        switch (value)
        {
            case ThriftInteger integer:
                WriteInteger(buffer, integer);
                break;
            case ThriftBinary binary:
                WriteI32(buffer, binary.Value.Length);
                buffer.Write(binary.Value);
                break;
            case ThriftStruct structure:
                WriteStructFields(buffer, structure, depth + 1);
                break;
            case ThriftList list:
                buffer.Write([(byte)list.ElementType]);
                WriteI32(buffer, list.Elements.Count);
                foreach (var element in list.Elements)
                {
                    RequireType(element, list.ElementType);
                    WriteValue(buffer, element, depth + 1);
                }

                break;
            case ThriftMap map:
                buffer.Write([(byte)map.KeyType, (byte)map.ValueType]);
                WriteI32(buffer, map.Entries.Count);
                foreach (var (key, entry) in map.Entries)
                {
                    RequireType(key, map.KeyType);
                    RequireType(entry, map.ValueType);
                    WriteValue(buffer, key, depth + 1);
                    WriteValue(buffer, entry, depth + 1);
                }

                break;
            default:
                throw new ArgumentException($"Unsupported Thrift value {value.GetType().Name}", nameof(value));
        }
    }

    public static void WriteStructFields(ArrayBufferWriter<byte> buffer, ThriftStruct structure, int depth)
    {
        Span<byte> id = stackalloc byte[sizeof(short)];
        foreach (var field in structure.Fields)
        {
            buffer.Write([(byte)field.Value.Type]);
            BinaryPrimitives.WriteInt16BigEndian(id, field.Id);
            buffer.Write(id);
            WriteValue(buffer, field.Value, depth);
        }

        buffer.Write([(byte)ThriftType.Stop]);
    }

    public static void WriteI32(ArrayBufferWriter<byte> buffer, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        buffer.Write(bytes);
    }

    public static void WriteI64(ArrayBufferWriter<byte> buffer, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        buffer.Write(bytes);
    }

    private static void WriteInteger(ArrayBufferWriter<byte> buffer, ThriftInteger integer)
    {
        switch (integer.Type)
        {
            case ThriftType.Bool:
            case ThriftType.Byte:
                buffer.Write([(byte)integer.Value]);
                break;
            case ThriftType.I16:
                Span<byte> bytes = stackalloc byte[sizeof(short)];
                BinaryPrimitives.WriteInt16BigEndian(bytes, (short)integer.Value);
                buffer.Write(bytes);
                break;
            case ThriftType.I32:
                WriteI32(buffer, (int)integer.Value);
                break;
            default:
                WriteI64(buffer, integer.Value);
                break;
        }
    }

    private static void RequireType(ThriftValue value, ThriftType expected)
    {
        if (value.Type != expected)
        {
            throw new ArgumentException($"Thrift element is {value.Type}, the collection holds {expected}", nameof(value));
        }
    }
}

internal ref struct ThriftReader(ReadOnlySpan<byte> data)
{
    public const int MaxDepth = 16;
    public const int MaxValues = 4096;

    private readonly ReadOnlySpan<byte> data = data;
    private int position;
    private int values;

    public readonly bool AtEnd => position == data.Length;

    public readonly int Remaining => data.Length - position;

    public ThriftMessage ReadMessage()
    {
        var nameLength = ReadI32();
        if (nameLength is <= 0 or > ThriftMessage.MaxNameLength)
        {
            throw new ThriftProtocolException($"Thrift message name length {nameLength} is out of range");
        }

        var nameBytes = Take(nameLength);
        if (nameBytes.ContainsAnyExceptInRange((byte)0x21, (byte)0x7E))
        {
            throw new ThriftProtocolException("Thrift message name is not printable ASCII");
        }

        var name = Encoding.ASCII.GetString(nameBytes);
        var type = Take(1)[0];
        if (type is not ((byte)ThriftMessageType.Call or (byte)ThriftMessageType.Reply))
        {
            throw new ThriftProtocolException($"Thrift message type {type} is not a call or reply");
        }

        var sequenceId = BinaryPrimitives.ReadInt64BigEndian(Take(sizeof(long)));
        var body = ReadStruct(depth: 1);
        return new ThriftMessage(name, (ThriftMessageType)type, sequenceId, body);
    }

    private ThriftStruct ReadStruct(int depth)
    {
        RequireDepth(depth);
        var fields = new List<ThriftField>();
        while (true)
        {
            var type = ReadType();
            if (type == ThriftType.Stop)
            {
                return new ThriftStruct(fields);
            }

            var id = BinaryPrimitives.ReadInt16BigEndian(Take(sizeof(short)));
            fields.Add(new ThriftField(id, ReadValue(type, depth)));
        }
    }

    private ThriftValue ReadValue(ThriftType type, int depth)
    {
        if (++values > MaxValues)
        {
            throw new ThriftProtocolException($"Thrift message holds more than {MaxValues} values");
        }

        return type switch
        {
            ThriftType.Bool or ThriftType.Byte => new ThriftInteger(type, (sbyte)Take(1)[0]),
            ThriftType.I16 => new ThriftInteger(type, BinaryPrimitives.ReadInt16BigEndian(Take(sizeof(short)))),
            ThriftType.I32 => new ThriftInteger(type, ReadI32()),
            ThriftType.I64 => new ThriftInteger(type, BinaryPrimitives.ReadInt64BigEndian(Take(sizeof(long)))),
            ThriftType.String => new ThriftBinary(Take(ReadLength()).ToArray()),
            ThriftType.Struct => ReadStruct(depth + 1),
            ThriftType.List => ReadList(depth + 1),
            ThriftType.Map => ReadMap(depth + 1),
            _ => throw new ThriftProtocolException($"Thrift type {type} cannot hold a value"),
        };
    }

    private ThriftList ReadList(int depth)
    {
        RequireDepth(depth);
        var elementType = ReadType();
        var count = ReadLength();
        var elements = new List<ThriftValue>(Math.Min(count, MaxValues));
        for (var index = 0; index < count; index++)
        {
            elements.Add(ReadValue(elementType, depth));
        }

        return new ThriftList(elementType, elements);
    }

    private ThriftMap ReadMap(int depth)
    {
        RequireDepth(depth);
        var keyType = ReadType();
        var valueType = ReadType();
        var count = ReadLength();
        var entries = new List<KeyValuePair<ThriftValue, ThriftValue>>(Math.Min(count, MaxValues));
        for (var index = 0; index < count; index++)
        {
            var key = ReadValue(keyType, depth);
            entries.Add(new KeyValuePair<ThriftValue, ThriftValue>(key, ReadValue(valueType, depth)));
        }

        return new ThriftMap(keyType, valueType, entries);
    }

    private ThriftType ReadType()
    {
        var type = (ThriftType)Take(1)[0];
        return Enum.IsDefined(type) ? type : throw new ThriftProtocolException($"Unknown Thrift type {(byte)type}");
    }

    private int ReadLength()
    {
        var length = ReadI32();
        return length >= 0 && length <= Remaining
            ? length
            : throw new ThriftProtocolException($"Thrift length {length} exceeds the {Remaining} remaining bytes");
    }

    private int ReadI32() => BinaryPrimitives.ReadInt32BigEndian(Take(sizeof(int)));

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count > Remaining)
        {
            throw new ThriftProtocolException("Thrift message ended early");
        }

        var slice = data.Slice(position, count);
        position += count;
        return slice;
    }

    private static void RequireDepth(int depth)
    {
        if (depth > MaxDepth)
        {
            throw new ThriftProtocolException($"Thrift message nests deeper than {MaxDepth}");
        }
    }
}
