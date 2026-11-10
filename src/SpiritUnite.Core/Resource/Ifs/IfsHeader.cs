using System.Buffers.Binary;
using System.Security.Cryptography;

namespace SpiritUnite.Resource.Ifs;

public sealed class IfsHeader
{
    public const int Size = 172;
    public const int RawChunkSize = 16384;

    private const uint Magic = 0x7366696E;
    private const int SectorShiftOffset = 0x0A;
    private const int DataEndOffset = 0x0C;
    private const int BetPositionOffset = 0x14;
    private const int HetPositionOffset = 0x1C;
    private const int ChunkMd5TablePositionOffset = 0x24;
    private const int ChunkBitmapPositionOffset = 0x2C;
    private const int HetSizeOffset = 0x34;
    private const int BetSizeOffset = 0x3C;
    private const int ChunkMd5TableSizeOffset = 0x44;
    private const int ChunkBitmapSizeOffset = 0x4C;
    private const int RawChunkSizeOffset = 0x54;
    private const int BetMd5Offset = 0x7C;
    private const int HetMd5Offset = 0x8C;
    private const int HeaderMd5Offset = 0x9C;
    private const int SectorSizeBase = 512;

    private readonly byte[] bytes;

    private IfsHeader(byte[] bytes) => this.bytes = bytes;

    public int SectorSize => SectorSizeBase << BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(SectorShiftOffset));
    public long DataEnd => Read(DataEndOffset);
    public long BetPosition => Read(BetPositionOffset);
    public long HetPosition => Read(HetPositionOffset);
    public long ChunkMd5TablePosition => Read(ChunkMd5TablePositionOffset);
    public long ChunkBitmapPosition => Read(ChunkBitmapPositionOffset);
    public long HetSize => Read(HetSizeOffset);
    public long BetSize => Read(BetSizeOffset);
    public long ChunkMd5TableSize => Read(ChunkMd5TableSizeOffset);
    public long ChunkBitmapSize => Read(ChunkBitmapSizeOffset);

    public static IfsHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size
            || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic
            || BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != Size)
        {
            throw new InvalidDataException("Not a nifs archive with a 172-byte header");
        }

        var header = new IfsHeader(data[..Size].ToArray());
        if (header.Read(RawChunkSizeOffset, sizeof(uint)) != RawChunkSize
            || header.DataEnd != header.ChunkMd5TablePosition
            || header.DataEnd % RawChunkSize != 0
            || header.ChunkMd5TableSize != (header.DataEnd / RawChunkSize + 1) * MD5.HashSizeInBytes
            || header.ChunkBitmapPosition != header.ChunkMd5TablePosition + header.ChunkMd5TableSize
            || header.ChunkBitmapSize != header.DataEnd / RawChunkSize
            || header.HetPosition + header.HetSize > header.BetPosition
            || header.BetPosition + header.BetSize > header.DataEnd)
        {
            throw new InvalidDataException("The nifs header does not describe the expected table and chunk layout");
        }

        return header;
    }

    public byte[] Relocated(long hetPosition, long betPosition, long dataEnd, ReadOnlySpan<byte> hetTable, ReadOnlySpan<byte> betTable)
    {
        var relocated = new IfsHeader(bytes.ToArray());
        var chunkCount = dataEnd / RawChunkSize;
        var md5TableSize = (chunkCount + 1) * MD5.HashSizeInBytes;
        relocated.Write(DataEndOffset, dataEnd);
        relocated.Write(HetPositionOffset, hetPosition);
        relocated.Write(BetPositionOffset, betPosition);
        relocated.Write(HetSizeOffset, hetTable.Length);
        relocated.Write(BetSizeOffset, betTable.Length);
        relocated.Write(ChunkMd5TablePositionOffset, dataEnd);
        relocated.Write(ChunkMd5TableSizeOffset, md5TableSize);
        relocated.Write(ChunkBitmapPositionOffset, dataEnd + md5TableSize);
        relocated.Write(ChunkBitmapSizeOffset, chunkCount);
        MD5.HashData(betTable, relocated.bytes.AsSpan(BetMd5Offset, MD5.HashSizeInBytes));
        MD5.HashData(hetTable, relocated.bytes.AsSpan(HetMd5Offset, MD5.HashSizeInBytes));
        MD5.HashData(relocated.bytes.AsSpan(0, HeaderMd5Offset), relocated.bytes.AsSpan(HeaderMd5Offset, MD5.HashSizeInBytes));
        return relocated.bytes;
    }

    private long Read(int offset, int width = sizeof(long)) => width == sizeof(long)
        ? BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(offset))
        : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));

    private void Write(int offset, long value) => BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(offset), value);
}
