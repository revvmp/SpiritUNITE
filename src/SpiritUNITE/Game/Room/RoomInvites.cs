using System.Collections.Concurrent;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed class RoomInvites(
    RoomManager rooms,
    FriendManager friends,
    FriendPresence presence,
    RoleInfoStore roles,
    GameData gameData,
    TimeProvider clock)
{
    public const int DenyInviteSeconds = (int)PB_MACRO_COM_DENY_INVITE_FROM_FRIEND_IN_NEXT_N_MINITUES.PbComDenyInviteFromFriendInNextNminitues * SecondsPerMinute;

    private const int SecondsPerMinute = 60;
    private const int ClientJoinRoomTypeInvite = (int)PB_ENUM_ENM_CLIENT_JOIN_ROOM_TYPE.PbEnmClientJoinRoomTypeInvite;

    private readonly ConcurrentDictionary<(ulong InviterUid, ulong InviteeUid), long> deniedUntil = new();

    public async Task<RoomOutcome> InviteAsync(ulong uid, ulong targetUid, int inviteType, int inviterType)
    {
        if (await CheckFriendAsync(uid, targetUid) is { } refusal)
        {
            return refusal;
        }

        if (rooms.FindRoomOf(uid) is not { } room)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom);
        }

        if (rooms.FindRoomOf(targetUid) is not null)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAlreadyInRoom);
        }

        if (room.IsMatching)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMatching);
        }

        if (deniedUntil.TryGetValue((uid, targetUid), out var until) && until > Now())
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattleInviteDenyLimit);
        }

        return new RoomOutcome().Push(targetUid, PB_ENUM_CS_MSGID_DEF.PbScidFriendInviteJoinRoomNtf, EmptyChildren.Fill(new pbSCFriendInviteJoinRoomNtf
        {
            RoomID = room.RoomID,
            RoomSeq = room.RoomSeq,
            RoomEntity = MatchRoom.RoomEntity,
            FromUid = uid,
            MapID = room.MapId,
            ClientJoinRoomType = IsClientJoinRoomType(inviteType) ? inviteType : ClientJoinRoomTypeInvite,
            InviterType = inviterType,
            RoleInviteInfo = BuildInviteInfo(uid),
        }));
    }

    public async Task<RoomOutcome> RequestJoinAsync(ulong uid, ulong friendUid, int inviterType)
    {
        if (await CheckFriendAsync(uid, friendUid) is { } refusal)
        {
            return refusal;
        }

        if (rooms.FindRoomOf(uid) is not null)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomAlreadyInRoom);
        }

        if (rooms.FindRoomOf(friendUid) is not { IsMatching: false })
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendCanNotReqJoin);
        }

        return new RoomOutcome().Push(friendUid, PB_ENUM_CS_MSGID_DEF.PbScidFriendRequestJoinRoomNtf, EmptyChildren.Fill(new pbSCFriendRequestJoinRoomNtf
        {
            Uid = uid,
            InviterType = inviterType,
            RoleInviteInfo = BuildInviteInfo(uid),
        }));
    }

    public RoomOutcome AllowJoin(ulong uid, ulong requesterUid, int inviterType)
    {
        if (!presence.IsOnline(requesterUid))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendNotOnline);
        }

        if (rooms.FindRoomOf(uid) is not { IsMatching: false } room || rooms.FindRoomOf(requesterUid) is not null)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendCanNotAllow);
        }

        return new RoomOutcome().Push(requesterUid, PB_ENUM_CS_MSGID_DEF.PbScidFriendAllowJoinRoomNtf, EmptyChildren.Fill(new pbSCFriendAllowJoinRoomNtf
        {
            RoomID = room.RoomID,
            RoomSeq = room.RoomSeq,
            RoomEntity = MatchRoom.RoomEntity,
            FromUid = uid,
            MapID = room.MapId,
            InviterType = inviterType,
            RoleInviteInfo = BuildInviteInfo(uid),
        }));
    }

    public RoomOutcome Deny(ulong uid, ulong inviterUid, bool denyForFiveMinutes)
    {
        if (denyForFiveMinutes)
        {
            deniedUntil[(inviterUid, uid)] = Now() + DenyInviteSeconds;
        }

        return new RoomOutcome().Push(inviterUid, PB_ENUM_CS_MSGID_DEF.PbScidFriendDenyInviteNtf, new pbSCFriendDenyInviteNtf
        {
            Uid = uid,
            ResultInfo = new pbResultInfo(),
        });
    }

    private async Task<RoomOutcome?> CheckFriendAsync(ulong uid, ulong otherUid)
    {
        if (otherUid == uid || otherUid == 0)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendCanNotInvite);
        }

        if (!presence.IsOnline(otherUid))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendNotOnline);
        }

        return await friends.FindFriendAsync(uid, otherUid) is null
            ? RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendNotFriend)
            : null;
    }

    private pbCOMDT_ROLE_INVITE_INFO BuildInviteInfo(ulong uid)
    {
        var role = roles.Require(uid);
        return EmptyChildren.Fill(new pbCOMDT_ROLE_INVITE_INFO
        {
            Uid = uid,
            RoleName = role.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            RoleGender = role.Gender,
            RankedDivisionInfo = RoleBrief.BuildRankedDivisionInfo(gameData, clock.GetUtcNow()),
            RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
        });
    }

    private long Now() => clock.GetUtcNow().ToUnixTimeSeconds();

    private static bool IsClientJoinRoomType(int inviteType) =>
        inviteType != (int)PB_ENUM_ENM_CLIENT_JOIN_ROOM_TYPE.PbEnmClientJoinRoomTypeNone
        && Enum.IsDefined((PB_ENUM_ENM_CLIENT_JOIN_ROOM_TYPE)inviteType);
}
