using System.Buffers.Binary;
using System.Text;
using SpiritUnite.Tools;

namespace SpiritUnite.Resource;

public sealed class StoredZip
{
    private const uint LocalHeaderSignature = 0x04034B50;
    private const uint CentralHeaderSignature = 0x02014B50;
    private const uint EndSignature = 0x06054B50;
    private const int LocalHeaderSize = 30;
    private const int CentralHeaderSize = 46;
    private const int EndSize = 22;
    private const int MaximumCommentLength = ushort.MaxValue;
    private const ushort StoredMethod = 0;
    private const ushort DataDescriptorFlag = 0x0008;
    private const ushort EncryptedFlag = 0x0001;

    private const int LocalFlagsOffset = 6;
    private const int LocalMethodOffset = 8;
    private const int LocalCrcOffset = 14;
    private const int LocalCompressedSizeOffset = 18;
    private const int LocalSizeOffset = 22;
    private const int LocalNameLengthOffset = 26;
    private const int LocalExtraLengthOffset = 28;
    private const int CentralMethodOffset = 10;
    private const int CentralCrcOffset = 16;
    private const int CentralCompressedSizeOffset = 20;
    private const int CentralSizeOffset = 24;
    private const int CentralNameLengthOffset = 28;
    private const int CentralExtraLengthOffset = 30;
    private const int CentralCommentLengthOffset = 32;
    private const int CentralLocalOffsetOffset = 42;
    private const int EndEntryCountOffset = 10;
    private const int EndDirectorySizeOffset = 12;
    private const int EndDirectoryOffsetOffset = 16;

    private readonly byte[] archive;
    private readonly List<CentralEntry> entries;
    private readonly int endOffset;

    private StoredZip(byte[] archive, List<CentralEntry> entries, int endOffset)
    {
        this.archive = archive;
        this.entries = entries;
        this.endOffset = endOffset;
    }

    public bool Contains(string name) => entries.Exists(entry => entry.Name == name);

    public static StoredZip Parse(byte[] archive)
    {
        var endOffset = FindEnd(archive);
        var end = archive.AsSpan(endOffset);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(end[EndEntryCountOffset..]);
        var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(end[EndDirectoryOffsetOffset..]));
        var entries = new List<CentralEntry>(count);
        for (var index = 0; index < count; index++)
        {
            var central = archive.AsSpan(offset);
            if (central.Length < CentralHeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(central) != CentralHeaderSignature)
            {
                throw new InvalidDataException("Bad zip central directory entry");
            }

            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(central[CentralNameLengthOffset..]);
            var length = CentralHeaderSize + nameLength
                + BinaryPrimitives.ReadUInt16LittleEndian(central[CentralExtraLengthOffset..])
                + BinaryPrimitives.ReadUInt16LittleEndian(central[CentralCommentLengthOffset..]);
            var name = Encoding.UTF8.GetString(central.Slice(CentralHeaderSize, nameLength));
            var localOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(central[CentralLocalOffsetOffset..]));
            entries.Add(new CentralEntry(name, archive[offset..(offset + length)], localOffset, null));
            offset += length;
        }

        return new StoredZip(archive, entries, endOffset);
    }

    public byte[] Read(string name)
    {
        var entry = entries.Find(candidate => candidate.Name == name) ?? throw new KeyNotFoundException($"Zip entry {name} not found");
        var (dataOffset, length) = LocalData(entry);
        return archive[dataOffset..(dataOffset + length)];
    }

    public void Replace(string name, byte[] data)
    {
        var index = entries.FindIndex(candidate => candidate.Name == name);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Zip entry {name} not found");
        }

        entries[index] = entries[index] with { Replacement = data };
    }

    public byte[] ToArray()
    {
        using var output = new MemoryStream(archive.Length);
        var centrals = new List<byte[]>(entries.Count);
        foreach (var entry in entries)
        {
            var (dataOffset, length) = LocalData(entry);
            var local = archive[entry.LocalOffset..dataOffset];
            var central = entry.Central.ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(central.AsSpan(CentralLocalOffsetOffset), checked((uint)output.Position));
            var data = entry.Replacement ?? archive.AsSpan(dataOffset, length).ToArray();
            if (entry.Replacement is { } replacement)
            {
                var crc = Crc32.Compute(replacement);
                var size = checked((uint)replacement.Length);
                foreach (var (header, crcOffset, compressedOffset, sizeOffset) in new[]
                {
                    (local, LocalCrcOffset, LocalCompressedSizeOffset, LocalSizeOffset),
                    (central, CentralCrcOffset, CentralCompressedSizeOffset, CentralSizeOffset),
                })
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(crcOffset), crc);
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(compressedOffset), size);
                    BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(sizeOffset), size);
                }
            }

            output.Write(local);
            output.Write(data);
            centrals.Add(central);
        }

        var directoryOffset = checked((uint)output.Position);
        foreach (var central in centrals)
        {
            output.Write(central);
        }

        var end = archive[endOffset..];
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(EndDirectorySizeOffset), checked((uint)output.Position - directoryOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(end.AsSpan(EndDirectoryOffsetOffset), directoryOffset);
        output.Write(end);
        return output.ToArray();
    }

    private (int DataOffset, int Length) LocalData(CentralEntry entry)
    {
        var local = archive.AsSpan(entry.LocalOffset);
        if (local.Length < LocalHeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(local) != LocalHeaderSignature
            || (BinaryPrimitives.ReadUInt16LittleEndian(local[LocalFlagsOffset..]) & (DataDescriptorFlag | EncryptedFlag)) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(local[LocalMethodOffset..]) != StoredMethod
            || BinaryPrimitives.ReadUInt16LittleEndian(entry.Central.AsSpan(CentralMethodOffset)) != StoredMethod)
        {
            throw new InvalidDataException($"Zip entry {entry.Name} is not a plain stored entry");
        }

        var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(local[LocalCompressedSizeOffset..]));
        var dataOffset = entry.LocalOffset + LocalHeaderSize
            + BinaryPrimitives.ReadUInt16LittleEndian(local[LocalNameLengthOffset..])
            + BinaryPrimitives.ReadUInt16LittleEndian(local[LocalExtraLengthOffset..]);
        if (dataOffset + length > archive.Length)
        {
            throw new InvalidDataException($"Zip entry {entry.Name} runs past the archive");
        }

        return (dataOffset, length);
    }

    private static int FindEnd(byte[] archive)
    {
        var lowest = Math.Max(0, archive.Length - EndSize - MaximumCommentLength);
        for (var offset = archive.Length - EndSize; offset >= lowest; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(offset)) == EndSignature)
            {
                return offset;
            }
        }

        throw new InvalidDataException("No zip end of central directory record");
    }

    private sealed record CentralEntry(string Name, byte[] Central, int LocalOffset, byte[]? Replacement);
}
