using Google.Protobuf;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed record RoomMessage(PB_ENUM_CS_MSGID_DEF MsgID, IMessage Message);

public sealed record RoomPush(ulong Uid, RoomMessage Message);

public sealed class RoomOutcome
{
    private readonly List<RoomMessage> replies = [];
    private readonly List<RoomPush> pushes = [];

    public PB_ENUM_RES_ERROR_CODE Result { get; private set; } = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    public IReadOnlyList<RoomMessage> Replies => replies;
    public IReadOnlyList<RoomPush> Pushes => pushes;
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    public int RequesterPushesBeforeResponse { get; private set; }

    public static RoomOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new() { Result = result };

    public RoomOutcome Reply(PB_ENUM_CS_MSGID_DEF msgID, IMessage message)
    {
        replies.Add(new RoomMessage(msgID, message));
        return this;
    }

    public RoomOutcome RespondHere(ulong requesterUid)
    {
        RequesterPushesBeforeResponse = pushes.Count(push => push.Uid == requesterUid);
        return this;
    }

    public RoomOutcome Push(ulong uid, PB_ENUM_CS_MSGID_DEF msgID, IMessage message)
    {
        pushes.Add(new RoomPush(uid, new RoomMessage(msgID, message)));
        return this;
    }

    public RoomOutcome PushAll(IEnumerable<ulong> uids, PB_ENUM_CS_MSGID_DEF msgID, IMessage message)
    {
        foreach (var uid in uids)
        {
            Push(uid, msgID, message);
        }

        return this;
    }
}
