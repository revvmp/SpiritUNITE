namespace SpiritUnite.Resource.Ifs;

public static class BitField
{
    private const int BitsPerByte = 8;
    private const int MaximumWordBits = 64;

    public static ulong Read(ReadOnlySpan<byte> data, long bitOffset, int width)
    {
        if (width is < 0 or > MaximumWordBits)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        ulong value = 0;
        for (var bit = 0; bit < width; bit++)
        {
            if (BitAt(data, bitOffset + bit))
            {
                value |= 1UL << bit;
            }
        }

        return value;
    }

    public static byte[] ReadBytes(ReadOnlySpan<byte> data, long bitOffset, int width)
    {
        var bytes = new byte[(width + BitsPerByte - 1) / BitsPerByte];
        for (var bit = 0; bit < width; bit++)
        {
            if (BitAt(data, bitOffset + bit))
            {
                bytes[bit / BitsPerByte] |= (byte)(1 << (bit % BitsPerByte));
            }
        }

        return bytes;
    }

    public static void Write(Span<byte> data, long bitOffset, int width, ulong value)
    {
        if (width is < 0 or > MaximumWordBits || (width < MaximumWordBits && value >> width != 0))
        {
            throw new InvalidDataException($"Value {value} does not fit a {width}-bit field");
        }

        for (var bit = 0; bit < width; bit++)
        {
            SetBit(data, bitOffset + bit, ((value >> bit) & 1) != 0);
        }
    }

    public static void WriteBytes(Span<byte> data, long bitOffset, int width, ReadOnlySpan<byte> value)
    {
        if (value.Length * BitsPerByte != width)
        {
            throw new InvalidDataException($"{value.Length} bytes do not fill a {width}-bit field");
        }

        for (var bit = 0; bit < width; bit++)
        {
            SetBit(data, bitOffset + bit, (value[bit / BitsPerByte] >> (bit % BitsPerByte) & 1) != 0);
        }
    }

    private static bool BitAt(ReadOnlySpan<byte> data, long bit) =>
        (data[checked((int)(bit / BitsPerByte))] >> (int)(bit % BitsPerByte) & 1) != 0;

    private static void SetBit(Span<byte> data, long bit, bool set)
    {
        var index = checked((int)(bit / BitsPerByte));
        var mask = (byte)(1 << (int)(bit % BitsPerByte));
        data[index] = set ? (byte)(data[index] | mask) : (byte)(data[index] & ~mask);
    }
}
