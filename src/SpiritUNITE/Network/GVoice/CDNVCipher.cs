using System.Buffers.Binary;

namespace SpiritUNITE.Network.GVoice;

public static class CDNVCipher
{
    private const int StateSize = 256;
    private const int RoomKeySize = sizeof(ulong);
    private const byte Crc8MaximReflectedPolynomial = 0x8C;

    public static byte[] RC4(ulong roomKey, ReadOnlySpan<byte> input)
    {
        Span<byte> key = stackalloc byte[RoomKeySize];
        BinaryPrimitives.WriteUInt64LittleEndian(key, roomKey);

        Span<byte> state = stackalloc byte[StateSize];
        for (var index = 0; index < StateSize; index++)
        {
            state[index] = (byte)index;
        }

        var j = 0;
        for (var i = 0; i < StateSize; i++)
        {
            j = (j + state[i] + key[i % RoomKeySize]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        var output = new byte[input.Length];
        int x = 0, y = 0;
        for (var index = 0; index < input.Length; index++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            output[index] = (byte)(input[index] ^ state[(state[x] + state[y]) & 0xFF]);
        }

        return output;
    }

    public static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var value in data[..Math.Min(data.Length, ushort.MaxValue)])
        {
            var remaining = value;
            for (var bit = 0; bit < 8; bit++)
            {
                var mixed = ((crc ^ remaining) & 1) != 0;
                crc >>= 1;
                if (mixed)
                {
                    crc ^= Crc8MaximReflectedPolynomial;
                }

                remaining >>= 1;
            }
        }

        return crc;
    }
}
