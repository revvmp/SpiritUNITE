using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace SpiritUnite.Databin;

public static class DatabinEnvelope
{
    private const int AesBlock = 16;
    private const int PrefixSize = 4;
    private const uint ChecksumSeed = 17;
    private const int MaximumPlainSize = 64 * 1024 * 1024;

    private static readonly byte[] Magic = [0x22, 0x4A, 0xEF];
    private static readonly byte[] MasterKey = Convert.FromHexString("9964b1b06b038d7fb77db6a754908b73");

    public static string EntryName(string databinName) => $"{NameHash(databinName)}.bytes";

    public static byte[] Decode(ReadOnlySpan<byte> envelope, string databinName)
    {
        if (envelope.Length < PrefixSize + AesBlock || !envelope[..Magic.Length].SequenceEqual(Magic))
        {
            throw new InvalidDataException($"{databinName} is not a 224aef databin envelope");
        }

        int padding = envelope[Magic.Length];
        if (padding is 0 or > AesBlock || (envelope.Length - PrefixSize) % AesBlock != 0)
        {
            throw new InvalidDataException($"{databinName} has a truncated AES payload or an invalid padding count");
        }

        using var aes = CreateAes(databinName);
        var payload = aes.DecryptCbc(envelope[PrefixSize..], new byte[AesBlock], PaddingMode.None).AsSpan(..^padding);
        if (BinaryPrimitives.ReadUInt32LittleEndian(payload) != Checksum(payload[sizeof(uint)..]))
        {
            throw new InvalidDataException($"{databinName} fails its checksum; the name does not match the envelope");
        }

        using var deflate = new DeflateStream(new MemoryStream(payload[sizeof(uint)..].ToArray()), CompressionMode.Decompress);
        using var plain = new MemoryStream();
        deflate.CopyTo(plain);
        if (plain.Length > MaximumPlainSize)
        {
            throw new InvalidDataException($"{databinName} inflates past {MaximumPlainSize} bytes");
        }

        return plain.ToArray();
    }

    public static byte[] Encode(ReadOnlySpan<byte> plain, string databinName)
    {
        using var compressed = new MemoryStream();
        compressed.Write(new byte[sizeof(uint)]);
        using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(plain);
        }

        var payloadLength = checked((int)compressed.Length);
        var padding = AesBlock - payloadLength % AesBlock;
        var padded = new byte[payloadLength + padding];
        compressed.GetBuffer().AsSpan(0, payloadLength).CopyTo(padded);
        padded.AsSpan(payloadLength).Fill((byte)padding);
        BinaryPrimitives.WriteUInt32LittleEndian(padded, Checksum(padded.AsSpan(sizeof(uint), payloadLength - sizeof(uint))));

        using var aes = CreateAes(databinName);
        var cipher = aes.EncryptCbc(padded, new byte[AesBlock], PaddingMode.None);
        var envelope = new byte[PrefixSize + cipher.Length];
        Magic.CopyTo(envelope, 0);
        envelope[Magic.Length] = (byte)padding;
        cipher.CopyTo(envelope, PrefixSize);
        return envelope;
    }

    private static Aes CreateAes(string databinName)
    {
        Span<byte> salt = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(salt, NameHash(databinName.ToUpperInvariant()));
        var key = new byte[MasterKey.Length];
        for (var index = 0; index < key.Length; index++)
        {
            key[index] = (byte)(MasterKey[index] ^ salt[index % salt.Length]);
        }

        var aes = Aes.Create();
        aes.Key = key;
        return aes;
    }

    private static uint NameHash(string name) => PolynomialHash(Encoding.ASCII.GetBytes(name), 0);

    private static uint Checksum(ReadOnlySpan<byte> data) => PolynomialHash(data, ChecksumSeed);

    private static uint PolynomialHash(ReadOnlySpan<byte> data, uint seed)
    {
        var hash = seed;
        foreach (var value in data)
        {
            hash = unchecked(31 * hash + value);
        }

        return hash;
    }
}
