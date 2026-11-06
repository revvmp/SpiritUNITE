using System.Text;

namespace SpiritUNITE.Network.Thrift;

public enum ThriftType : byte
{
    Stop = 0,
    Bool = 2,
    Byte = 3,
    I16 = 6,
    I32 = 8,
    I64 = 10,
    String = 11,
    Struct = 12,
    Map = 13,
    List = 15,
}

public sealed class ThriftProtocolException(string message) : Exception(message);

public abstract record ThriftValue(ThriftType Type);

public sealed record ThriftInteger : ThriftValue
{
    public ThriftInteger(ThriftType type, long value) : base(type)
    {
        if (type is not (ThriftType.Bool or ThriftType.Byte or ThriftType.I16 or ThriftType.I32 or ThriftType.I64))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Not a Thrift integer type");
        }

        Value = value;
    }

    public long Value { get; }

    public static ThriftInteger Byte(sbyte value) => new(ThriftType.Byte, value);

    public static ThriftInteger I32(int value) => new(ThriftType.I32, value);

    public static ThriftInteger I64(long value) => new(ThriftType.I64, value);
}

public sealed record ThriftBinary(byte[] Value) : ThriftValue(ThriftType.String)
{
    public static ThriftBinary String(string value) => new(Encoding.UTF8.GetBytes(value));
}

public sealed record ThriftList(ThriftType ElementType, IReadOnlyList<ThriftValue> Elements) : ThriftValue(ThriftType.List);

public sealed record ThriftMap(
    ThriftType KeyType,
    ThriftType ValueType,
    IReadOnlyList<KeyValuePair<ThriftValue, ThriftValue>> Entries) : ThriftValue(ThriftType.Map);

public readonly record struct ThriftField(short Id, ThriftValue Value);

public sealed record ThriftStruct(IReadOnlyList<ThriftField> Fields) : ThriftValue(ThriftType.Struct)
{
    public ThriftValue? Find(short id) => Fields.FirstOrDefault(field => field.Id == id).Value;

    public ThriftStruct? FindStruct(short id) => Find(id) as ThriftStruct;

    public long? FindInteger(short id, ThriftType type) =>
        Find(id) is ThriftInteger integer && integer.Type == type ? integer.Value : null;

    public string? FindString(short id) =>
        Find(id) is ThriftBinary binary ? Encoding.UTF8.GetString(binary.Value) : null;

    public ThriftStruct Only(Func<short, bool> keepField) =>
        new(Fields.Where(field => keepField(field.Id)).ToArray());
}
