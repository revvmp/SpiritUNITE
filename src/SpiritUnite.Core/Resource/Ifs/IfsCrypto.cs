using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace SpiritUnite.Resource.Ifs;

public static class IfsCrypto
{
    private const int CryptTableLength = 0x500;
    private const int CryptTableSlotStride = 0x100;
    private const int EncryptionTableOffset = 0x400;
    private const uint CryptTableSeed = 0x00100001;
    private const uint FirstHashSeed = 0x7FED7FED;
    private const uint SecondHashSeed = 0xEEEEEEEE;
    private const uint KeyStepAddend = 0x11111111;
    private const int TableKeyHashType = 3;
    private const uint JenkinsPrimarySeed = 1;
    private const uint JenkinsSecondarySeed = 2;
    private const uint JenkinsInitial = 0xDEADBEEF;
    private const int JenkinsBlock = 12;

    private static readonly uint[] CryptTable = BuildCryptTable();

    public static readonly uint HashTableKey = HashString("(hash table)", TableKeyHashType);
    public static readonly uint BlockTableKey = HashString("(block table)", TableKeyHashType);

    public static uint HashString(string text, int hashType)
    {
        uint seed1 = FirstHashSeed, seed2 = SecondHashSeed;
        foreach (var character in Encoding.ASCII.GetBytes(text.ToUpperInvariant()))
        {
            seed1 = CryptTable[hashType * CryptTableSlotStride + character] ^ (seed1 + seed2);
            seed2 = character + seed1 + seed2 + (seed2 << 5) + 3;
        }

        return seed1;
    }

    public static void Decrypt(Span<byte> data, uint key)
    {
        var seed = SecondHashSeed;
        for (var offset = 0; offset + sizeof(uint) <= data.Length; offset += sizeof(uint))
        {
            seed += CryptTable[EncryptionTableOffset + (key & 0xFF)];
            var plain = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]) ^ (key + seed);
            BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], plain);
            key = NextKey(key);
            seed = plain + seed + (seed << 5) + 3;
        }
    }

    public static void Encrypt(Span<byte> data, uint key)
    {
        var seed = SecondHashSeed;
        for (var offset = 0; offset + sizeof(uint) <= data.Length; offset += sizeof(uint))
        {
            seed += CryptTable[EncryptionTableOffset + (key & 0xFF)];
            var plain = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
            BinaryPrimitives.WriteUInt32LittleEndian(data[offset..], plain ^ (key + seed));
            key = NextKey(key);
            seed = plain + seed + (seed << 5) + 3;
        }
    }

    public static ulong NameHash64(string archivePath)
    {
        var name = Encoding.UTF8.GetBytes(archivePath.Replace('/', '\\').ToLowerInvariant());
        var (secondary, primary) = HashLittle2(name, JenkinsSecondarySeed, JenkinsPrimarySeed);
        return ((ulong)primary << 32) | secondary;
    }

    private static uint NextKey(uint key) => ((~key << 0x15) + KeyStepAddend) | (key >> 0x0B);

    private static uint[] BuildCryptTable()
    {
        var table = new uint[CryptTableLength];
        var seed = CryptTableSeed;
        for (var index = 0; index < CryptTableSlotStride; index++)
        {
            for (var slot = index; slot < CryptTableLength; slot += CryptTableSlotStride)
            {
                seed = (seed * 125 + 3) % 0x2AAAAB;
                var high = (seed & 0xFFFF) << 16;
                seed = (seed * 125 + 3) % 0x2AAAAB;
                table[slot] = high | (seed & 0xFFFF);
            }
        }

        return table;
    }

    private static (uint C, uint B) HashLittle2(ReadOnlySpan<byte> key, uint initialC, uint initialB)
    {
        var length = key.Length;
        uint a, b, c;
        a = b = c = JenkinsInitial + (uint)length + initialC;
        c += initialB;
        var offset = 0;
        while (length > JenkinsBlock)
        {
            a += BinaryPrimitives.ReadUInt32LittleEndian(key[offset..]);
            b += BinaryPrimitives.ReadUInt32LittleEndian(key[(offset + 4)..]);
            c += BinaryPrimitives.ReadUInt32LittleEndian(key[(offset + 8)..]);
            Mix(ref a, ref b, ref c);
            length -= JenkinsBlock;
            offset += JenkinsBlock;
        }

        if (length == 0)
        {
            return (c, b);
        }

        Span<byte> tail = stackalloc byte[JenkinsBlock];
        key[offset..].CopyTo(tail);
        a += BinaryPrimitives.ReadUInt32LittleEndian(tail);
        b += BinaryPrimitives.ReadUInt32LittleEndian(tail[4..]);
        c += BinaryPrimitives.ReadUInt32LittleEndian(tail[8..]);
        Final(ref a, ref b, ref c);
        return (c, b);
    }

    private static void Mix(ref uint a, ref uint b, ref uint c)
    {
        a -= c; a ^= BitOperations.RotateLeft(c, 4); c += b;
        b -= a; b ^= BitOperations.RotateLeft(a, 6); a += c;
        c -= b; c ^= BitOperations.RotateLeft(b, 8); b += a;
        a -= c; a ^= BitOperations.RotateLeft(c, 16); c += b;
        b -= a; b ^= BitOperations.RotateLeft(a, 19); a += c;
        c -= b; c ^= BitOperations.RotateLeft(b, 4); b += a;
    }

    private static void Final(ref uint a, ref uint b, ref uint c)
    {
        c ^= b; c -= BitOperations.RotateLeft(b, 14);
        a ^= c; a -= BitOperations.RotateLeft(c, 11);
        b ^= a; b -= BitOperations.RotateLeft(a, 25);
        c ^= b; c -= BitOperations.RotateLeft(b, 16);
        a ^= c; a -= BitOperations.RotateLeft(c, 4);
        b ^= a; b -= BitOperations.RotateLeft(a, 14);
        c ^= b; c -= BitOperations.RotateLeft(b, 24);
    }
}
