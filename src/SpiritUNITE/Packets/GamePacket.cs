using Google.Protobuf;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets;

public sealed record GamePacket(int MsgID, ulong Uid, int ClientSeq, byte[] PkgData)
{
    private const int MsgIDField = 1;
    private const int UidField = 2;
    private const int SvrPkgSeqField = 4;
    private const int ClientSeqField = 5;

    public static GamePacket Create(PB_ENUM_CS_MSGID_DEF msgID, IMessage message) =>
        new((int)msgID, Uid: 0, ClientSeq: 0, message.ToByteArray());

    public GamePacket Reply(PB_ENUM_CS_MSGID_DEF msgID, IMessage message) =>
        new((int)msgID, Uid, ClientSeq, message.ToByteArray());

    public static GamePacket Parse(ReadOnlyMemory<byte> data)
    {
        try
        {
            var input = new CodedInputStream(data.ToArray());
            var (msgID, uid, clientSeq) = ParseHead(input.ReadBytes());
            return new GamePacket(msgID, uid, clientSeq, ParseBody(input.ReadBytes(), msgID));
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new InvalidDataException($"Game packet is not valid protobuf: {exception.Message}");
        }
    }

    public byte[] ToByteArray(int svrPkgSeq)
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteBytes(BuildHead(svrPkgSeq));
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

    private static byte[] ParseBody(ByteString body, int msgID)
    {
        var input = body.CreateCodedInput();

        for (var tag = input.ReadTag(); tag != 0; tag = input.ReadTag())
        {
            if (WireFormat.GetTagFieldNumber(tag) == msgID)
            {
                return input.ReadBytes().ToByteArray();
            }

            input.SkipLastField();
        }

        return [];
    }

    private ByteString BuildHead(int svrPkgSeq)
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteTag(MsgIDField, WireFormat.WireType.Varint);
        output.WriteInt32(MsgID);
        output.WriteTag(UidField, WireFormat.WireType.Varint);
        output.WriteUInt64(Uid);
        output.WriteTag(SvrPkgSeqField, WireFormat.WireType.Varint);
        output.WriteInt32(svrPkgSeq);
        output.WriteTag(ClientSeqField, WireFormat.WireType.Varint);
        output.WriteInt32(ClientSeq);
        output.Flush();
        return ByteString.CopyFrom(buffer.ToArray());
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
