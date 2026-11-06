using Google.Protobuf;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets;

public sealed record GamePacket(int MsgID, ulong Uid, int ClientSeq, byte[] PkgData)
{
    private const int MsgIDField = 1;
    private const int UidField = 2;
    private const int VersionField = 3;
    private const int SvrPkgSeqField = 4;
    private const int ClientSeqField = 5;
    private const int IsPkgMemField = 7;
    private const int PkgMem = 1;
    private const int MsgIDFromBody = 0;
    private const ulong NoUid = 0;
    private const int NoClientSeq = 0;
    private const int NoVersion = 0;

    public int Version { get; init; }

    public int? SvrPkgSeq { get; init; }

    public bool MsgIDInBodyOnly { get; init; }

    public static GamePacket Create(PB_ENUM_CS_MSGID_DEF msgID, IMessage message) =>
        new((int)msgID, Uid: 0, ClientSeq: 0, message.ToByteArray());

    public GamePacket Reply(PB_ENUM_CS_MSGID_DEF msgID, IMessage message) =>
        new((int)msgID, Uid, ClientSeq, message.ToByteArray());

    public static GamePacket Parse(ReadOnlyMemory<byte> data)
    {
        try
        {
            var input = new CodedInputStream(data.ToArray());
            var (headMsgID, uid, clientSeq) = ParseHead(input.ReadBytes());
            var (msgID, pkgData) = ParseBody(input.ReadBytes(), headMsgID);
            return new GamePacket(msgID, uid, clientSeq, pkgData);
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new InvalidDataException($"Game packet is not valid protobuf: {exception.Message}");
        }
    }

    public byte[] ToByteArray(int svrPkgSeq, bool isPkgMem = false)
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteBytes(BuildHead(svrPkgSeq, isPkgMem));
        output.WriteBytes(BuildBody());
        output.Flush();
        return buffer.ToArray();
    }

    public byte[] ToRelayByteArray()
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteBytes(BuildRelayHead());
        output.WriteBytes(BuildBody());
        output.Flush();
        return buffer.ToArray();
    }

    private static (int MsgID, ulong Uid, int ClientSeq) ParseHead(ByteString head)
    {
        var input = head.CreateCodedInput();
        var (msgID, uid, clientSeq) = (0, 0UL, 0);

        for (var tag = input.ReadTag(); tag != 0; tag = input.ReadTag())
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case MsgIDField:
                    msgID = input.ReadInt32();
                    break;
                case UidField:
                    uid = input.ReadUInt64();
                    break;
                case ClientSeqField:
                    clientSeq = input.ReadInt32();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return (msgID, uid, clientSeq);
    }

    private static (int MsgID, byte[] PkgData) ParseBody(ByteString body, int headMsgID)
    {
        var input = body.CreateCodedInput();

        for (var tag = input.ReadTag(); tag != 0; tag = input.ReadTag())
        {
            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            if (headMsgID == MsgIDFromBody && WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
            {
                return (fieldNumber, input.ReadBytes().ToByteArray());
            }

            if (fieldNumber == headMsgID)
            {
                return (headMsgID, input.ReadBytes().ToByteArray());
            }

            input.SkipLastField();
        }

        return (headMsgID, []);
    }

    private ByteString BuildHead(int svrPkgSeq, bool isPkgMem)
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteTag(MsgIDField, WireFormat.WireType.Varint);
        output.WriteInt32(MsgID);
        if (Uid != NoUid)
        {
            output.WriteTag(UidField, WireFormat.WireType.Varint);
            output.WriteUInt64(Uid);
        }

        WriteVersion(output);
        output.WriteTag(SvrPkgSeqField, WireFormat.WireType.Varint);
        output.WriteInt32(svrPkgSeq);
        if (ClientSeq != NoClientSeq)
        {
            output.WriteTag(ClientSeqField, WireFormat.WireType.Varint);
            output.WriteInt32(ClientSeq);
        }

        if (isPkgMem)
        {
            output.WriteTag(IsPkgMemField, WireFormat.WireType.Varint);
            output.WriteInt32(PkgMem);
        }

        output.Flush();
        return ByteString.CopyFrom(buffer.ToArray());
    }

    private ByteString BuildRelayHead()
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        if (!MsgIDInBodyOnly)
        {
            output.WriteTag(MsgIDField, WireFormat.WireType.Varint);
            output.WriteInt32(MsgID);
        }

        WriteVersion(output);
        if (SvrPkgSeq is { } svrPkgSeq)
        {
            output.WriteTag(SvrPkgSeqField, WireFormat.WireType.Varint);
            output.WriteInt32(svrPkgSeq);
        }

        if (ClientSeq != NoClientSeq)
        {
            output.WriteTag(ClientSeqField, WireFormat.WireType.Varint);
            output.WriteInt32(ClientSeq);
        }

        output.Flush();
        return ByteString.CopyFrom(buffer.ToArray());
    }

    private void WriteVersion(CodedOutputStream output)
    {
        if (Version != NoVersion)
        {
            output.WriteTag(VersionField, WireFormat.WireType.Varint);
            output.WriteInt32(Version);
        }
    }

    private ByteString BuildBody()
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteTag(MsgID, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(PkgData));
        output.Flush();
        return ByteString.CopyFrom(buffer.ToArray());
    }
}
