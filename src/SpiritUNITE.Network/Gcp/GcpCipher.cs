using System.Security.Cryptography;

namespace SpiritUNITE.Network.Gcp;

public sealed class GcpCipher : IDisposable
{
    public const int KeySize = 16;

    private const int BlockSize = 16;
    private const int LongTailThreshold = 10;
    private const int PadLengthSize = 1;

    private static readonly byte[] InitializationVector =
        [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];

    private static ReadOnlySpan<byte> TailMarker => "tsf4g"u8;

    private readonly Aes aes = Aes.Create();

    public GcpCipher(ReadOnlySpan<byte> key)
    {
        aes.SetKey(key);
    }

    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext.IsEmpty || ciphertext.Length % BlockSize != 0)
        {
            throw new GcpProtocolException($"Ciphertext length {ciphertext.Length} is not a whole number of blocks");
        }

        var padded = aes.DecryptCbc(ciphertext, InitializationVector, PaddingMode.None);
        var tailStart = padded.Length - PadLengthSize - TailMarker.Length;
        if (tailStart < 0 || !padded.AsSpan(tailStart, TailMarker.Length).SequenceEqual(TailMarker))
        {
            throw new GcpProtocolException("Decrypted body has no padding marker");
        }

        var plaintextLength = padded.Length - padded[^1];
        if (plaintextLength <= 0 || PaddingLengthFor(plaintextLength) != padded[^1])
        {
            throw new GcpProtocolException($"Decrypted body has invalid padding length {padded[^1]}");
        }

        return padded[..plaintextLength];
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.IsEmpty)
        {
            throw new ArgumentException("GCP cannot encrypt an empty body", nameof(plaintext));
        }

        var paddingLength = PaddingLengthFor(plaintext.Length);
        var padded = new byte[plaintext.Length + paddingLength];
        plaintext.CopyTo(padded);

        var tailStart = padded.Length - PadLengthSize - TailMarker.Length;
        RandomNumberGenerator.Fill(padded.AsSpan(plaintext.Length, tailStart - plaintext.Length));
        TailMarker.CopyTo(padded.AsSpan(tailStart));
        padded[^1] = (byte)paddingLength;

        return aes.EncryptCbc(padded, InitializationVector, PaddingMode.None);
    }

    public void Dispose() => aes.Dispose();

    private static int PaddingLengthFor(int plaintextLength)
    {
        var remainder = plaintextLength % BlockSize;
        var tailSize = remainder <= LongTailThreshold ? BlockSize : 2 * BlockSize;
        return tailSize - remainder;
    }
}
