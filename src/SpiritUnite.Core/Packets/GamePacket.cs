using Google.Protobuf;
using SpiritUnite.Auth;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets;

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

    internal LoginSession? Login { get; init; }

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

    public byte[] ToByteArray(int svrPkgSeq, bool isPkgMem = false) =>
        Encode(relay: false, svrPkgSeq, isPkgMem);

    public byte[] ToRelayByteArray() => Encode(relay: true, SvrPkgSeq, isPkgMem: false);

    private byte[] Encode(bool relay, int? svrPkgSeq, bool isPkgMem)
    {
        var writesMsgID = !relay || !MsgIDInBodyOnly;
        var writesUid = !relay && Uid != NoUid;
        var headSize = (writesMsgID ? CodedOutputStream.ComputeTagSize(MsgIDField) + CodedOutputStream.ComputeInt32Size(MsgID) : 0)
            + (writesUid ? CodedOutputStream.ComputeTagSize(UidField) + CodedOutputStream.ComputeUInt64Size(Uid) : 0)
            + (Version != NoVersion ? CodedOutputStream.ComputeTagSize(VersionField) + CodedOutputStream.ComputeInt32Size(Version) : 0)
            + (svrPkgSeq is { } sequence ? CodedOutputStream.ComputeTagSize(SvrPkgSeqField) + CodedOutputStream.ComputeInt32Size(sequence) : 0)
            + (ClientSeq != NoClientSeq ? CodedOutputStream.ComputeTagSize(ClientSeqField) + CodedOutputStream.ComputeInt32Size(ClientSeq) : 0)
            + (isPkgMem ? CodedOutputStream.ComputeTagSize(IsPkgMemField) + CodedOutputStream.ComputeInt32Size(PkgMem) : 0);
        var bodySize = checked(CodedOutputStream.ComputeTagSize(MsgID) + CodedOutputStream.ComputeLengthSize(PkgData.Length) + PkgData.Length);
        var packet = new byte[checked(CodedOutputStream.ComputeLengthSize(headSize) + headSize + CodedOutputStream.ComputeLengthSize(bodySize) + bodySize)];
        using var output = new CodedOutputStream(packet);
        output.WriteLength(headSize);
        if (writesMsgID)
        {
            output.WriteTag(MsgIDField, WireFormat.WireType.Varint);
            output.WriteInt32(MsgID);
        }

        if (writesUid)
        {
            output.WriteTag(UidField, WireFormat.WireType.Varint);
            output.WriteUInt64(Uid);
        }

        if (Version != NoVersion)
        {
            output.WriteTag(VersionField, WireFormat.WireType.Varint);
            output.WriteInt32(Version);
        }

        if (svrPkgSeq is { } writtenSequence)
        {
            output.WriteTag(SvrPkgSeqField, WireFormat.WireType.Varint);
            output.WriteInt32(writtenSequence);
        }

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

        output.WriteLength(bodySize);
        output.WriteTag(MsgID, WireFormat.WireType.LengthDelimited);
        output.WriteLength(PkgData.Length);
        output.Flush();
        PkgData.CopyTo(packet.AsSpan((int)output.Position));
        return packet;
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
}
