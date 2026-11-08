using Google.Protobuf;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets;

public static class NoTagPB
{
    private const int FrapBootCCSyncTypeField = 2;
    private const int FrapBootInfoKFrapsNoField = 1;
    private const int FrameCmdPkgTypeField = 1;
    private const int GamingSyncInfoSyncTypeField = 3;
    private const int GamingSyncInfoObjIDField = 6;
    private const int GamingUpermsgIsFromAIField = 3;
    private const int VarintMaxBytes = 10;
    private const int VarintPayloadBits = 7;
    private const int VarintContinuation = 0x80;
    private const int VarintPayloadMask = 0x7F;
    private const int TagFieldShift = 3;
    private const int TagWireTypeMask = 7;
    private const int MaxFieldNumber = (1 << 29) - 1;
    private const int Fixed64Bytes = 8;
    private const int Fixed32Bytes = 4;

    public static byte[] ToNoTagByteArray(this pbSCFrapBootInfoNtf ntf) => Write(output =>
    {
        output.WriteInt32(ntf.Session);
        output.WriteInt32(ntf.SpareFrap.Count);
        foreach (var frame in ntf.SpareFrap)
        {
            output.WriteBytes(ByteString.CopyFrom(frame.ToNoTagByteArray()));
        }
    });

    public static byte[] ToNoTagByteArray(this pbSCFrapBootSingleNtf frame) => Write(output =>
    {
        output.WriteInt32(frame.KFrapsNo);
        output.WriteBytes(frame.InfoBuff);
    });

    public static byte[] ToNoTagByteArray(this pbCSDT_FRAPBOOT_INFO info) => Write(output =>
    {
        output.WriteInt32(info.BootInfo.Count);
        foreach (var command in info.BootInfo)
        {
            output.WriteBytes(ByteString.CopyFrom(command.ToNoTagByteArray()));
        }

        if (info.KFrapsNo != 0)
        {
            output.WriteTag(FrapBootInfoKFrapsNoField, WireFormat.WireType.Varint);
            output.WriteInt32(info.KFrapsNo);
        }
    });

    public static byte[] ToNoTagByteArray(this pbCSDT_FRAPBOOT_CC command) => Write(output =>
    {
        output.WriteInt32(command.ObjID);
        output.WriteBytes(command.Buff);
        if (command.SyncType != 0)
        {
            output.WriteTag(FrapBootCCSyncTypeField, WireFormat.WireType.Varint);
            output.WriteInt32(command.SyncType);
        }
    });

    public static byte[] ToNoTagByteArray(this FrameCmdPkg command) => Write(output =>
    {
        output.WriteInt32(command.IntParam1);
        if (command.Type != default)
        {
            output.WriteTag(FrameCmdPkgTypeField, WireFormat.WireType.Varint);
            output.WriteEnum((int)command.Type);
        }

        WriteTaggedInt32(output, FrameCmdPkg.IntParam2FieldNumber, command.IntParam2);
        WriteTaggedInt32(output, FrameCmdPkg.IntParam3FieldNumber, command.IntParam3);
        WriteTaggedInt32(output, FrameCmdPkg.IntParam4FieldNumber, command.IntParam4);
        WriteTaggedInt32(output, FrameCmdPkg.IntParam5FieldNumber, command.IntParam5);
        WriteTaggedInt32(output, FrameCmdPkg.IntParam6FieldNumber, command.IntParam6);
        WriteTaggedInt32(output, FrameCmdPkg.IntParam7FieldNumber, command.IntParam7);
        if (command.Int64Param1 != 0)
        {
            output.WriteTag(FrameCmdPkg.Int64Param1FieldNumber, WireFormat.WireType.Varint);
            output.WriteInt64(command.Int64Param1);
        }
    });

    private static void WriteTaggedInt32(CodedOutputStream output, int fieldNumber, int value)
    {
        if (value != 0)
        {
            output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
            output.WriteInt32(value);
        }
    }

    public static pbCSGamingUpermsgReq ParseGamingUpermsgReq(ReadOnlySpan<byte> data, int maxUperInfo)
    {
        var reader = new NoTagReader(data);
        var count = reader.ReadCount(maxUperInfo);
        var request = new pbCSGamingUpermsgReq();
        for (var index = 0; index < count; index++)
        {
            request.UperInfo.Add(ParseGamingSyncInfo(reader.ReadLengthDelimited()));
        }

        while (!reader.AtEnd)
        {
            var (field, wireType) = reader.ReadTag();
            if (field == GamingUpermsgIsFromAIField && wireType == WireFormat.WireType.Varint)
            {
                request.IsFromAI = reader.ReadInt32();
            }
            else
            {
                reader.Skip(wireType);
            }
        }

        return request;
    }

    public static int ReadFrameCmdPkgIntParam1(ReadOnlySpan<byte> buff) => new NoTagReader(buff).ReadInt32();

    private static pbCSDT_GAMING_SYNC_INFO ParseGamingSyncInfo(ReadOnlySpan<byte> data)
    {
        var reader = new NoTagReader(data);
        var info = new pbCSDT_GAMING_SYNC_INFO
        {
            CmdSeq = reader.ReadInt32(),
            IsActiveAction = reader.ReadInt32(),
            Buff = ByteString.CopyFrom(reader.ReadLengthDelimited()),
        };
        while (!reader.AtEnd)
        {
            var (field, wireType) = reader.ReadTag();
            if (field == GamingSyncInfoSyncTypeField && wireType == WireFormat.WireType.Varint)
            {
                info.SyncType = reader.ReadInt32();
            }
            else if (field == GamingSyncInfoObjIDField && wireType == WireFormat.WireType.Varint)
            {
                info.ObjID = reader.ReadInt32();
            }
            else
            {
                reader.Skip(wireType);
            }
        }

        return info;
    }

    private static byte[] Write(Action<CodedOutputStream> write)
    {
        using var buffer = new MemoryStream();
        using (var output = new CodedOutputStream(buffer, leaveOpen: true))
        {
            write(output);
        }

        return buffer.ToArray();
    }

    private ref struct NoTagReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private int position;

        public readonly bool AtEnd => position >= data.Length;

        public int ReadInt32() => unchecked((int)ReadVarint());

        public int ReadCount(int max)
        {
            var count = ReadVarint();
            return count <= (ulong)max ? (int)count : throw new InvalidDataException($"Tagless list of {count} entries is over the limit of {max}");
        }

        public ReadOnlySpan<byte> ReadLengthDelimited()
        {
            var length = ReadVarint();
            if (length > (ulong)(data.Length - position))
            {
                throw new InvalidDataException("Tagless length runs past the end of the message");
            }

            var slice = data.Slice(position, (int)length);
            position += (int)length;
            return slice;
        }

        public (int Field, WireFormat.WireType WireType) ReadTag()
        {
            var tag = ReadVarint();
            var field = tag >> TagFieldShift;
            if (field is 0 or > MaxFieldNumber)
            {
                throw new InvalidDataException($"Tagless tail has an invalid field number {field}");
            }

            return ((int)field, (WireFormat.WireType)(int)(tag & TagWireTypeMask));
        }

        public void Skip(WireFormat.WireType wireType)
        {
            switch (wireType)
            {
                case WireFormat.WireType.Varint:
                    ReadVarint();
                    break;
                case WireFormat.WireType.LengthDelimited:
                    ReadLengthDelimited();
                    break;
                case WireFormat.WireType.Fixed64:
                    Advance(Fixed64Bytes);
                    break;
                case WireFormat.WireType.Fixed32:
                    Advance(Fixed32Bytes);
                    break;
                default:
                    throw new InvalidDataException($"Tagless tail has an unsupported wire type {wireType}");
            }
        }

        private void Advance(int count)
        {
            if (count > data.Length - position)
            {
                throw new InvalidDataException("Tagless field runs past the end of the message");
            }

            position += count;
        }

        private ulong ReadVarint()
        {
            ulong value = 0;
            for (var index = 0; index < VarintMaxBytes; index++)
            {
                if (position >= data.Length)
                {
                    throw new InvalidDataException("Tagless varint runs past the end of the message");
                }

                var next = data[position++];
                value |= (ulong)(next & VarintPayloadMask) << (index * VarintPayloadBits);
                if ((next & VarintContinuation) == 0)
                {
                    return value;
                }
            }

            throw new InvalidDataException("Tagless varint is longer than ten bytes");
        }
    }
}
