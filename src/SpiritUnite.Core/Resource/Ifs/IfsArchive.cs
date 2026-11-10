using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SpiritUnite.Resource.Ifs;

public sealed record IfsEntry(int Index, long FilePos, long FileSize, long CmpSize, uint Flags, byte[] Md5);

public sealed class IfsArchive : IDisposable
{
    public const uint FileExists = 0x80000000;
    public const uint FileCompress = 0x00000200;

    private const uint FileEncrypted = 0x00010000;
    private const uint FileSingleUnit = 0x01000000;
    private const uint FileSectorCrc = 0x04000000;
    private const byte CompressionZlib = 0x02;
    private const int ExtTableHeaderSize = 12;
    private const uint HetSignature = 0x1A544548;
    private const uint BetSignature = 0x1A544542;
    private const int HetHeaderSize = 32;
    private const int HetNameHashBits = 64;
    private const int BetHeaderWords = 21;
    private const int BetHeaderSize = BetHeaderWords * sizeof(uint);
    private const int BetFieldCount = 5;
    private const int BetFieldPositionWord = 3;
    private const int BetFieldWidthWord = 9;
    private const int BetHashArraySizeWord = 18;
    private const int BetHash2BitsWord = 17;
    private const int MaximumExtractSize = 512 * 1024 * 1024;

    private readonly FileStream file;
    private readonly byte[] hetNameHashes;
    private readonly byte[] hetIndexes;
    private readonly int hetIndexEntryBits;
    private readonly int hetIndexBits;
    private readonly byte[] betBody;
    private readonly int betEntryBits;
    private readonly (int Position, int Width)[] betFields;
    private readonly byte[] betTable;
    private readonly ulong[] nameHash2s;
    private readonly int hash2Bits;

    public IfsArchive(string path)
    {
        Path = path;
        file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.RandomAccess);
        Length = file.Length;
        Header = IfsHeader.Parse(ReadAt(0, IfsHeader.Size));
        if (Header.ChunkBitmapPosition + Header.ChunkBitmapSize != Length)
        {
            throw new InvalidDataException("The nifs chunk bitmap does not end the archive");
        }

        HetTable = ReadAt(Header.HetPosition, checked((int)Header.HetSize));
        BetTable = ReadAt(Header.BetPosition, checked((int)Header.BetSize));
        var het = DecryptExtTable(HetTable, HetSignature, IfsCrypto.HashTableKey);
        betBody = DecryptExtTable(BetTable, BetSignature, IfsCrypto.BlockTableKey);

        var hetTotalCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(het.AsSpan(8)));
        if (BinaryPrimitives.ReadUInt32LittleEndian(het.AsSpan(12)) != HetNameHashBits)
        {
            throw new InvalidDataException("Only 64-bit HET name hashes are supported");
        }

        hetIndexEntryBits = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(het.AsSpan(16)));
        hetIndexBits = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(het.AsSpan(24)));
        var indexTableSize = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(het.AsSpan(28)));
        hetNameHashes = het.AsSpan(HetHeaderSize, hetTotalCount).ToArray();
        hetIndexes = het.AsSpan(HetHeaderSize + hetTotalCount, indexTableSize).ToArray();

        var entryCount = checked((int)BetWord(1));
        betEntryBits = checked((int)BetWord(2));
        betFields = Enumerable.Range(0, BetFieldCount)
            .Select(field => (checked((int)BetWord(BetFieldPositionWord + field)), checked((int)BetWord(BetFieldWidthWord + field))))
            .ToArray();
        hash2Bits = checked((int)BetWord(BetHash2BitsWord));
        var tableSize = (entryCount * betEntryBits + 7) / 8;
        betTable = betBody.AsSpan(BetHeaderSize, tableSize).ToArray();
        var hashes = betBody.AsSpan(BetHeaderSize + tableSize, checked((int)BetWord(BetHashArraySizeWord))).ToArray();
        nameHash2s = new ulong[entryCount];
        var entries = new IfsEntry[entryCount];
        for (var index = 0; index < entryCount; index++)
        {
            nameHash2s[index] = BitField.Read(hashes, (long)index * hash2Bits, hash2Bits);
            var bitBase = (long)index * betEntryBits;
            entries[index] = new IfsEntry(
                index,
                (long)BitField.Read(betTable, bitBase + betFields[0].Position, betFields[0].Width),
                (long)BitField.Read(betTable, bitBase + betFields[1].Position, betFields[1].Width),
                (long)BitField.Read(betTable, bitBase + betFields[2].Position, betFields[2].Width),
                (uint)BitField.Read(betTable, bitBase + betFields[3].Position, betFields[3].Width),
                BitField.ReadBytes(betTable, bitBase + betFields[4].Position, betFields[4].Width));
        }

        Entries = entries;
    }

    public string Path { get; }
    public long Length { get; }
    public IfsHeader Header { get; }
    public byte[] HetTable { get; }
    public byte[] BetTable { get; }
    public IReadOnlyList<IfsEntry> Entries { get; }

    public IfsEntry? Find(string archivePath)
    {
        var fileHash = IfsCrypto.NameHash64(archivePath) | (1UL << 63);
        var nameHash1 = (byte)(fileHash >> 56);
        var nameHash2 = fileHash & ((1UL << 56) - 1);
        var count = hetNameHashes.Length;
        var start = (int)(fileHash % (ulong)count);
        var slot = start;
        while (hetNameHashes[slot] != 0)
        {
            if (hetNameHashes[slot] == nameHash1)
            {
                var index = (int)BitField.Read(hetIndexes, (long)slot * hetIndexEntryBits, hetIndexBits);
                if (index < Entries.Count && nameHash2s[index] == (nameHash2 & HashMask(hash2Bits)))
                {
                    return Entries[index];
                }
            }

            slot = (slot + 1) % count;
            if (slot == start)
            {
                break;
            }
        }

        return null;
    }

    public byte[] Extract(IfsEntry entry)
    {
        if ((entry.Flags & (FileEncrypted | FileSingleUnit | FileSectorCrc)) != 0 || entry.FileSize > MaximumExtractSize)
        {
            throw new InvalidDataException($"Entry {entry.Index} uses flags {entry.Flags:x} or a size this reader does not support");
        }

        var raw = ReadAt(entry.FilePos, checked((int)entry.CmpSize));
        var size = checked((int)entry.FileSize);
        if ((entry.Flags & FileCompress) == 0)
        {
            return raw[..size];
        }

        var sectorSize = Header.SectorSize;
        var sectorCount = (size + sectorSize - 1) / sectorSize;
        var output = new byte[size];
        for (var sector = 0; sector < sectorCount; sector++)
        {
            var start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(sector * sizeof(uint))));
            var end = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan((sector + 1) * sizeof(uint))));
            var expected = Math.Min(sectorSize, size - sector * sectorSize);
            DecompressSector(raw.AsSpan(start, end - start), output.AsSpan(sector * sectorSize, expected));
        }

        if (!MD5.HashData(output).AsSpan().SequenceEqual(entry.Md5))
        {
            throw new InvalidDataException($"Entry {entry.Index} does not match its MD5");
        }

        return output;
    }

    public byte[] Compress(ReadOnlySpan<byte> data)
    {
        var sectorSize = Header.SectorSize;
        var sectorCount = (data.Length + sectorSize - 1) / sectorSize;
        var tableSize = (sectorCount + 1) * sizeof(uint);
        using var output = new MemoryStream();
        output.Write(new byte[tableSize]);
        var offsets = new uint[sectorCount + 1];
        for (var sector = 0; sector < sectorCount; sector++)
        {
            offsets[sector] = checked((uint)output.Position);
            var plain = data.Slice(sector * sectorSize, Math.Min(sectorSize, data.Length - sector * sectorSize));
            var compressed = CompressSector(plain);
            output.Write(compressed.Length < plain.Length ? compressed : plain);
        }

        offsets[sectorCount] = checked((uint)output.Position);
        var result = output.ToArray();
        for (var index = 0; index < offsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(index * sizeof(uint)), offsets[index]);
        }

        return result;
    }

    public byte[] BetTableWith(IReadOnlyCollection<IfsEntry> replacements)
    {
        var body = betBody.ToArray();
        var table = body.AsSpan(BetHeaderSize, betTable.Length);
        foreach (var entry in replacements)
        {
            var bitBase = (long)entry.Index * betEntryBits;
            BitField.Write(table, bitBase + betFields[0].Position, betFields[0].Width, checked((ulong)entry.FilePos));
            BitField.Write(table, bitBase + betFields[1].Position, betFields[1].Width, checked((ulong)entry.FileSize));
            BitField.Write(table, bitBase + betFields[2].Position, betFields[2].Width, checked((ulong)entry.CmpSize));
            BitField.Write(table, bitBase + betFields[3].Position, betFields[3].Width, entry.Flags);
            BitField.WriteBytes(table, bitBase + betFields[4].Position, betFields[4].Width, entry.Md5);
        }

        IfsCrypto.Encrypt(body, IfsCrypto.BlockTableKey);
        var encoded = BetTable.ToArray();
        body.CopyTo(encoded.AsSpan(ExtTableHeaderSize));
        return encoded;
    }

    public byte[] ReadAt(long position, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var got = RandomAccess.Read(file.SafeFileHandle, buffer.AsSpan(read), position + read);
            if (got == 0)
            {
                throw new EndOfStreamException($"The archive ends before byte {position + count}");
            }

            read += got;
        }

        return buffer;
    }

    public void Dispose() => file.Dispose();

    private uint BetWord(int word) => BinaryPrimitives.ReadUInt32LittleEndian(betBody.AsSpan(word * sizeof(uint)));

    private static byte[] DecryptExtTable(byte[] raw, uint signature, uint key)
    {
        if (raw.Length < ExtTableHeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(raw) != signature
            || BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(8)) + ExtTableHeaderSize != raw.Length)
        {
            throw new InvalidDataException($"Bad nifs table with signature {signature:x}");
        }

        var body = raw.AsSpan(ExtTableHeaderSize).ToArray();
        IfsCrypto.Decrypt(body, key);
        return body;
    }

    private static ulong HashMask(int bits) => bits >= 64 ? ulong.MaxValue : (1UL << bits) - 1;

    private static void DecompressSector(ReadOnlySpan<byte> sector, Span<byte> destination)
    {
        if (sector.Length == destination.Length)
        {
            sector.CopyTo(destination);
            return;
        }

        if (sector.IsEmpty || sector[0] != CompressionZlib)
        {
            throw new InvalidDataException("Unsupported nifs sector compression");
        }

        using var zlib = new ZLibStream(new MemoryStream(sector[1..].ToArray()), CompressionMode.Decompress);
        zlib.ReadExactly(destination);
        if (zlib.ReadByte() != -1)
        {
            throw new InvalidDataException("A nifs sector decompressed to more than its size");
        }
    }

    private static byte[] CompressSector(ReadOnlySpan<byte> plain)
    {
        using var output = new MemoryStream();
        output.WriteByte(CompressionZlib);
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(plain);
        }

        return output.ToArray();
    }
}
