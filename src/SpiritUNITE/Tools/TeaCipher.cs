using System.Buffers.Binary;

namespace SpiritUNITE.Tools;

public static class TeaCipher
{
    public const int KeyLength = 16;
    private const int BlockLength = 8;
    private const int Rounds = 16;
    private const uint Delta = 0x9E3779B9;
    private const int PadLengthMask = 7;
    private const int HeaderLength = 1;
    private const int SaltLength = 2;
    private const int ZeroLength = 7;
    private const int MinimumCipherLength = 2 * BlockLength;

    public static byte[]? Decrypt(ReadOnlySpan<byte> cipherText, ReadOnlySpan<byte> key)
    {
        if (key.Length != KeyLength || cipherText.Length < MinimumCipherLength || cipherText.Length % BlockLength != 0)
        {
            return null;
        }

        var keyWords = ReadKey(key);
        var plain = new byte[cipherText.Length];
        Span<byte> chained = stackalloc byte[BlockLength];
        Span<byte> previousCipher = stackalloc byte[BlockLength];
        chained.Clear();
        previousCipher.Clear();
        for (var offset = 0; offset < cipherText.Length; offset += BlockLength)
        {
            var block = cipherText.Slice(offset, BlockLength);
            for (var index = 0; index < BlockLength; index++)
            {
                chained[index] ^= block[index];
            }

            DecryptBlock(chained, keyWords);
            for (var index = 0; index < BlockLength; index++)
            {
                plain[offset + index] = (byte)(chained[index] ^ previousCipher[index]);
            }

            block.CopyTo(previousCipher);
        }

        var padLength = plain[0] & PadLengthMask;
        var dataStart = HeaderLength + padLength + SaltLength;
        var dataLength = plain.Length - dataStart - ZeroLength;
        if (dataLength < 0 || plain.AsSpan(plain.Length - ZeroLength).ContainsAnyExcept((byte)0))
        {
            return null;
        }

        return plain.AsSpan(dataStart, dataLength).ToArray();
    }

    private static uint[] ReadKey(ReadOnlySpan<byte> key) =>
    [
        BinaryPrimitives.ReadUInt32BigEndian(key),
        BinaryPrimitives.ReadUInt32BigEndian(key[4..]),
        BinaryPrimitives.ReadUInt32BigEndian(key[8..]),
        BinaryPrimitives.ReadUInt32BigEndian(key[12..]),
    ];

    private static void DecryptBlock(Span<byte> block, uint[] key)
    {
        var y = BinaryPrimitives.ReadUInt32BigEndian(block);
        var z = BinaryPrimitives.ReadUInt32BigEndian(block[4..]);
        var sum = unchecked(Delta * (uint)Rounds);
        for (var round = 0; round < Rounds; round++)
        {
            z -= ((y << 4) + key[2]) ^ (y + sum) ^ ((y >> 5) + key[3]);
            y -= ((z << 4) + key[0]) ^ (z + sum) ^ ((z >> 5) + key[1]);
            sum -= Delta;
        }

        BinaryPrimitives.WriteUInt32BigEndian(block, y);
        BinaryPrimitives.WriteUInt32BigEndian(block[4..], z);
    }
}
