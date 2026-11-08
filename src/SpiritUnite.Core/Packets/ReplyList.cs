using Google.Protobuf;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets;

public sealed class ReplyList(GamePacket request)
{
    private readonly List<GamePacket> replies = [];

    public void Add(PB_ENUM_CS_MSGID_DEF msgID, IMessage message) => replies.Add(request.Reply(msgID, message));

    public void Add(int msgID, byte[] pkgData) => replies.Add(request with { MsgID = msgID, PkgData = pkgData });

    public void Add(GamePacket packet) => replies.Add(packet with { Uid = request.Uid, ClientSeq = request.ClientSeq });

    public void AddRange(IEnumerable<GamePacket> packets)
    {
        foreach (var packet in packets)
        {
            Add(packet);
        }
    }

    public GamePacket[] ToArray() => [.. replies];
}
