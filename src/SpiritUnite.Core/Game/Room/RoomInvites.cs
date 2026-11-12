using System.Collections.Concurrent;
using SpiritUnite.Battle;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Video;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Room;

public sealed class RoomInvites(
    RoomManager rooms,
    FriendManager friends,
    FriendPresence presence,
    GuildRepository guilds,
    TenGamesRepository tenGames,
    RoleInfoStore roles,
    RankedManager ranked,
    ObserveManager observes,
    VideoPlayback videoPlayback,
    GameData gameData,
    TimeProvider clock)
{
    public const int DenyInviteSeconds = (int)PB_MACRO_COM_DENY_INVITE_FROM_FRIEND_IN_NEXT_N_MINITUES.PbComDenyInviteFromFriendInNextNminitues * SecondsPerMinute;

    private const int SecondsPerMinute = 60;
    private const int ClientJoinRoomTypeInvite = (int)PB_ENUM_ENM_CLIENT_JOIN_ROOM_TYPE.PbEnmClientJoinRoomTypeInvite;

    private readonly ConcurrentDictionary<(ulong InviterUid, ulong InviteeUid), long> deniedUntil = new();
    private readonly ConcurrentDictionary<(ulong InviterUid, ulong InviteeUid), long> reinviteAllowedAt = new();

    public async Task<RoomOutcome> InviteAsync(ulong uid, ulong targetUid, int inviteType, int inviterType)
    {
        if (await CheckInviterRelationAsync(uid, targetUid, inviterType) is { } refusal)
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

        if (observes.ObserveStateOf(targetUid) != PB_ENUM_ENM_OBSERVE_STATE.PbEnmObserveNull)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattleInviteObserve);
        }

        if (videoPlayback.IsPlayingVideo(targetUid))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattleInvitePlayVideo);
        }

        if (room.IsMatching)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRoomMatching);
        }

        if (deniedUntil.TryGetValue((uid, targetUid), out var until) && until > Now())
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattleInviteDenyLimit);
        }

        var now = Now();
        if (reinviteAllowedAt.TryGetValue((uid, targetUid), out var allowedAt) && allowedAt > now)
        {
            return RoomOutcome.Refused(
                PB_ENUM_RES_ERROR_CODE.PbErrBattleInviteLimit,
                new pbArgList { Args = { new pbArgData { Value = new pbArgDataValue { IntValue = (int)(allowedAt - now) } } } });
        }

        reinviteAllowedAt[(uid, targetUid)] = now + gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalFriendInviteDenyCd);
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
        if (await CheckInviterRelationAsync(uid, friendUid, inviterType) is { } refusal)
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

    private async Task<RoomOutcome?> CheckInviterRelationAsync(ulong uid, ulong otherUid, int inviterType)
    {
        if (otherUid == uid || otherUid == 0)
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendCanNotInvite);
        }

        if (!presence.IsOnline(otherUid))
        {
            return RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendNotOnline);
        }

        return (PB_ENUM_ENM_FRIEND_INVITER_TYPE)inviterType switch
        {
            PB_ENUM_ENM_FRIEND_INVITER_TYPE.PbEnmFriendInviterTypeGuild => await AreInOneGuildAsync(uid, otherUid)
                ? null
                : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist),
            PB_ENUM_ENM_FRIEND_INVITER_TYPE.PbEnmFriendInviterTypeTengames => await AreInOneTenGamesTeamAsync(uid, otherUid)
                ? null
                : RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTenGamesOhterNotInTeam),
            _ => await friends.FindFriendAsync(uid, otherUid) is null
                ? RoomOutcome.Refused(PB_ENUM_RES_ERROR_CODE.PbErrFriendNotFriend)
                : null,
        };
    }

    private async Task<bool> AreInOneGuildAsync(ulong uid, ulong otherUid) =>
        await guilds.FindMemberAsync(uid) is { } member
        && await guilds.FindMemberAsync(otherUid) is { } otherMember
        && member.GuildID == otherMember.GuildID;

    private async Task<bool> AreInOneTenGamesTeamAsync(ulong uid, ulong otherUid) =>
        await tenGames.FindMemberAsync(uid) is { } member
        && await tenGames.FindMemberAsync(otherUid) is { } otherMember
        && member.TeamID == otherMember.TeamID;

    private pbCOMDT_ROLE_INVITE_INFO BuildInviteInfo(ulong uid)
    {
        var role = roles.Require(uid);
        return EmptyChildren.Fill(new pbCOMDT_ROLE_INVITE_INFO
        {
            Uid = uid,
            RoleName = role.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            RoleGender = role.Gender,
            RankedDivisionInfo = RoleBrief.BuildRankedDivisionInfo(ranked.CurrentSeasonId, ranked.PositionOf(uid).RankedDivision),
            RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
        });
    }

    private long Now() => clock.GetUtcNow().ToUnixTimeSeconds();

    private static bool IsClientJoinRoomType(int inviteType) =>
        inviteType != (int)PB_ENUM_ENM_CLIENT_JOIN_ROOM_TYPE.PbEnmClientJoinRoomTypeNone
        && Enum.IsDefined((PB_ENUM_ENM_CLIENT_JOIN_ROOM_TYPE)inviteType);
}
