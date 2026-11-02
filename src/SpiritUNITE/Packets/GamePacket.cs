using Google.Protobuf;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets;

public sealed record GamePacket(int MessageId, ulong Uid, int ClientSequence, byte[] Payload)
{
    private const int MessageIdField = 1;
    private const int UidField = 2;
    private const int ClientSequenceField = 5;

    public static GamePacket Create(PB_ENUM_CS_MSGID_DEF messageId, IMessage message) =>
        new((int)messageId, 0, 0, message.ToByteArray());

    public GamePacket Reply(PB_ENUM_CS_MSGID_DEF messageId, IMessage message) =>
        new((int)messageId, Uid, ClientSequence, message.ToByteArray());

    public static GamePacket Parse(ReadOnlyMemory<byte> data)
    {
        try
        {
            var input = new CodedInputStream(data.ToArray());
            var (messageId, uid, clientSequence) = ParseHead(input.ReadBytes());
            return new GamePacket(messageId, uid, clientSequence, ParseBody(input.ReadBytes(), messageId));
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new InvalidDataException($"Game packet is not valid protobuf: {exception.Message}");
        }
    }

    public byte[] ToByteArray()
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteBytes(BuildHead());
        output.WriteBytes(BuildBody());
        output.Flush();
        return buffer.ToArray();
    }

    private static (int MessageId, ulong Uid, int ClientSequence) ParseHead(ByteString head)
    {
        var input = head.CreateCodedInput();
        var (messageId, uid, clientSequence) = (0, 0UL, 0);

        for (var tag = input.ReadTag(); tag != 0; tag = input.ReadTag())
        {
            switch (WireFormat.GetTagFieldNumber(tag))
            {
                case MessageIdField:
                    messageId = input.ReadInt32();
                    break;
                case UidField:
                    uid = input.ReadUInt64();
                    break;
                case ClientSequenceField:
                    clientSequence = input.ReadInt32();
                    break;
                default:
                    input.SkipLastField();
                    break;
            }
        }

        return (messageId, uid, clientSequence);
    }

    private static byte[] ParseBody(ByteString body, int messageId)
    {
        var input = body.CreateCodedInput();

        for (var tag = input.ReadTag(); tag != 0; tag = input.ReadTag())
        {
            if (WireFormat.GetTagFieldNumber(tag) == messageId)
            {
                return input.ReadBytes().ToByteArray();
            }

            input.SkipLastField();
        }

        return [];
    }

    private ByteString BuildHead()
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteTag(MessageIdField, WireFormat.WireType.Varint);
        output.WriteInt32(MessageId);
        output.WriteTag(UidField, WireFormat.WireType.Varint);
        output.WriteUInt64(Uid);
        output.WriteTag(ClientSequenceField, WireFormat.WireType.Varint);
        output.WriteInt32(ClientSequence);
        output.Flush();
        return ByteString.CopyFrom(buffer.ToArray());
    }

    private ByteString BuildBody()
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteTag(MessageId, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(Payload));
        output.Flush();
        return ByteString.CopyFrom(buffer.ToArray());
    }
}
