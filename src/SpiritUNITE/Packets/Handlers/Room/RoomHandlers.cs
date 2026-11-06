using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Auth;
using SpiritUNITE.Battle;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Network;

namespace SpiritUNITE.Packets.Handlers.Room;

public sealed class RoomHandlers
{
    private const int Accepted = 1;
    private const int DenyForFiveMinutes = 1;

    private readonly RoomManager rooms;
    private readonly RoomInvites invites;
    private readonly DeskManager desks;
    private readonly RoleSessions sessions;
    private readonly FriendPresence presence;
    private readonly ProfileManager profiles;
    private readonly SettingsManager settings;
    private readonly AccountManager accounts;
    private readonly RoleInfoStore roles;
    private readonly ActivitySchedule activitySchedule;
    private readonly ActivityTasks activityTasks;
    private readonly GVoiceOptions gVoice;
    private readonly TimeProvider clock;
    private readonly ILogger<RoomHandlers> logger;

    public RoomHandlers(
        RoomManager rooms,
        RoomInvites invites,
        DeskManager desks,
        RelayManager relays,
        RoleSessions sessions,
        FriendPresence presence,
        ProfileManager profiles,
        SettingsManager settings,
        AccountManager accounts,
        RoleInfoStore roles,
        ActivitySchedule activitySchedule,
        ActivityTasks activityTasks,
        IOptions<GVoiceOptions> gVoice,
        TimeProvider clock,
        ILogger<RoomHandlers> logger)
    {
        this.rooms = rooms;
        this.invites = invites;
        this.desks = desks;
        this.sessions = sessions;
        this.presence = presence;
        this.profiles = profiles;
        this.settings = settings;
        this.accounts = accounts;
        this.roles = roles;
        this.activitySchedule = activitySchedule;
        this.activityTasks = activityTasks;
        this.gVoice = gVoice.Value;
        this.clock = clock;
        this.logger = logger;
        sessions.RoleOffline += OnRoleOfflineAsync;
        desks.DeskClosed += presence.LeaveGameAsync;
        relays.RelayFinished += presence.LeaveGameAsync;
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCreateRoomReq)]
    public async Task<GamePacket[]> OnCreateRoomReq(GamePacket request)
    {
        var create = pbCSCreateRoomReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.Create(
            request.Uid,
            new RoomCreateRequest(
                create.MapMode,
                create.MapID,
                create.SubMapMode,
                create.FromSystemType,
                MapList(create.UsableMapInfo),
                create.PveChallengeSelectNode),
            await LoadMemberProfileAsync(request.Uid));
        logger.LogInformation(
            "Create room for uid {Uid}: MapMode {MapMode}, MapID {MapID}, SubMapMode {SubMapMode}, ClientCreateType {ClientCreateType}, {MapCount} usable maps: {Result}",
            request.Uid,
            create.MapMode,
            create.MapID,
            create.SubMapMode,
            create.ClientCreateType,
            create.UsableMapInfo?.MapList.Count ?? 0,
            outcome.Result);
        if (!outcome.Succeeded)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidJoinRoomNtf, RoomNotices.JoinRefused(outcome.Result))];
        }

        await MarkInRoomAsync(request.Uid);
        var dailyActivityInfo = await RoomDailyActivity.BuildDailyActivityInfoAsync(
            activitySchedule,
            activityTasks,
            roles.Require(request.Uid),
            clock.GetUtcNow());
        return await DeliverAsync(
            request,
            outcome,
            [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomDailyActivityRewardNtf, RoomNotices.DailyActivityReward(dailyActivityInfo))],
            [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomRttNtf, RoomNotices.RoomRtt(gVoice, sessions.Rtt(request.Uid)))]);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShowidJoinRoomReq)]
    public async Task<GamePacket[]> OnShowidJoinRoomReq(GamePacket request)
    {
        var join = pbCSShowidJoinRoomReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.JoinByShowId(
            request.Uid,
            join.RoomShowId,
            MapList(join.UsableMapInfo),
            await LoadMemberProfileAsync(request.Uid));
        logger.LogInformation("Uid {Uid} joins room show id {RoomShowId}: {Result}", request.Uid, join.RoomShowId, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidShowidJoinRoomRsp, new pbSCShowidJoinRoomRsp
        {
            Result = Result(outcome),
        });
        return await CompleteJoinAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidJoinRoomReq)]
    public async Task<GamePacket[]> OnJoinRoomReq(GamePacket request)
    {
        var join = pbCSJoinRoomReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.JoinById(
            request.Uid,
            join.RoomID,
            join.RoomSeq,
            MapList(join.UsableMapInfo),
            await LoadMemberProfileAsync(request.Uid));
        logger.LogInformation(
            "Uid {Uid} joins room {RoomID} seq {RoomSeq} from uid {ContextUid}: {Result}",
            request.Uid,
            join.RoomID,
            join.RoomSeq,
            join.ContextUid,
            outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidJoinRoomRsp, new pbSCJoinRoomRsp
        {
            Result = Result(outcome),
            ContextUid = join.ContextUid,
        });
        return await CompleteJoinAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQuitRoomReq)]
    public async Task<GamePacket[]> OnQuitRoomReq(GamePacket request)
    {
        pbCSQuitRoomReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.Quit(request.Uid);
        logger.LogInformation("Uid {Uid} quits the room: {Result}", request.Uid, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQuitRoomRsp, new pbSCQuitRoomRsp { ResultInfo = Result(outcome) });
        if (outcome.Succeeded)
        {
            await MarkIdleAsync(request.Uid);
        }

        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidKickRoomMemReq)]
    public async Task<GamePacket[]> OnKickRoomMemReq(GamePacket request)
    {
        var kick = pbCSKickRoomMemReq.Parser.ParseFrom(request.PkgData).KickMemberInfo ?? new pbCOMDT_KICK_ROOMMEMBER_INFO();
        var targetUid = rooms.FindRoomOf(request.Uid)?.FindMemberAt(kick.ClientCamp, kick.PosOfCamp)?.Uid;
        var outcome = rooms.Kick(request.Uid, kick.ClientCamp, kick.PosOfCamp);
        logger.LogInformation(
            "Uid {Uid} kicks camp {CampClient} pos {PosOfCamp} (uid {TargetUid}): {Result}",
            request.Uid,
            kick.ClientCamp,
            kick.PosOfCamp,
            targetUid,
            outcome.Result);
        if (!outcome.Succeeded)
        {
            return [CommErrcode(request, outcome.Result)];
        }

        await MarkIdleAsync(targetUid!.Value);
        return await DeliverAsync(request, outcome);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomStartGameReq)]
    public async Task<GamePacket[]> OnRoomStartGameReq(GamePacket request)
    {
        pbCSRoomStartGameReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.StartGame(request.Uid);
        logger.LogInformation("Uid {Uid} starts matching: {Result}", request.Uid, outcome.Result);
        if (outcome.Succeeded && rooms.FindMatchConfirm(request.Uid) is { } ticket)
        {
            logger.LogInformation(
                "Room {RoomID} matched against bots; waiting {ConfirmTimeoutMs} ms for every member to confirm",
                ticket.RoomID,
                PveMatch.ConfirmTimeoutMs);
            _ = ExpireMatchConfirmAsync(ticket);
            return await DeliverAsync(request, outcome);
        }

        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomStartGameRsp, new pbSCRoomStartGameRsp
        {
            ResultInfo = Result(outcome),
        });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomConfirmReq)]
    public async Task<GamePacket[]> OnRoomConfirmReq(GamePacket request)
    {
        pbCSRoomConfirmReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.ConfirmMatch(request.Uid);
        logger.LogInformation("Uid {Uid} confirms the match: {Result}", request.Uid, outcome.Result);
        if (!outcome.Succeeded)
        {
            return
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomConfirmRsp, new pbSCRoomConfirmRsp
                {
                    Uid = request.Uid,
                    FromGameEntity = MatchRoom.GameEntity,
                    ResultInfo = Result(outcome),
                }),
            ];
        }

        var confirmReplies = await DeliverAsync(request, outcome);
        if (outcome.StartedGame is not { } room)
        {
            return confirmReplies;
        }

        var deskOutcome = await desks.BeginPveDeskAsync(room);
        foreach (var member in room.Members)
        {
            await (deskOutcome.Succeeded ? MarkInGameAsync(member.Uid) : MarkIdleAsync(member.Uid));
        }

        return [.. confirmReplies, .. await DeliverAsync(request, deskOutcome)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCancelMatchReq)]
    public async Task<GamePacket[]> OnCancelMatchReq(GamePacket request)
    {
        var outcome = rooms.CancelMatch(request.Uid);
        logger.LogInformation("Uid {Uid} cancels matching: {Result}", request.Uid, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCancelMatchRsp, new pbSCCancelMatchRsp { ResultInfo = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomChgMapReq)]
    public async Task<GamePacket[]> OnRoomChgMapReq(GamePacket request)
    {
        var mapId = pbCSRoomChgMapReq.Parser.ParseFrom(request.PkgData).MapId;
        var outcome = rooms.ChangeMap(request.Uid, mapId);
        logger.LogInformation("Uid {Uid} changes the room map to {MapId}: {Result}", request.Uid, mapId, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomChgMapRsp, new pbSCRoomChgMapRsp { ResultInfo = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomChgSubModeReq)]
    public async Task<GamePacket[]> OnRoomChgSubModeReq(GamePacket request)
    {
        var subMode = pbCSRoomChgSubModeReq.Parser.ParseFrom(request.PkgData).SubMode;
        var outcome = rooms.ChangeSubMode(request.Uid, subMode);
        logger.LogInformation("Uid {Uid} changes the room sub mode to {SubMode}: {Result}", request.Uid, subMode, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomChgSubModeRsp, new pbSCRoomChgSubModeRsp { Result = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomChgRankedTeamMemberNumTypeReq)]
    public async Task<GamePacket[]> OnRoomChgRankedTeamMemberNumTypeReq(GamePacket request)
    {
        var numType = pbCSRoomChgRankedTeamMemberNumTypeReq.Parser.ParseFrom(request.PkgData).RankedTeamMemberNumType;
        var outcome = rooms.ChangeRankedTeamMemberNumType(request.Uid, numType);
        logger.LogInformation("Uid {Uid} changes the ranked team size to {NumType}: {Result}", request.Uid, numType, outcome.Result);
        var response = request.Reply(
            PB_ENUM_CS_MSGID_DEF.PbScidRoomChgRankedTeamMemberNumTypeRsp,
            new pbSCRoomChgRankedTeamMemberNumTypeRsp { Result = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomChgTypeReq)]
    public async Task<GamePacket[]> OnRoomChgTypeReq(GamePacket request)
    {
        var change = pbCSRoomChgTypeReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.ChangeRoomType(request.Uid, change.RoomType, change.ManMachine);
        logger.LogInformation(
            "Uid {Uid} changes the room type to {RoomType}, ManMachine {ManMachine}: {Result}",
            request.Uid,
            change.RoomType,
            change.ManMachine,
            outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomChgTypeRsp, new pbSCRoomChgTypeRsp { ResultInfo = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomChgMasterReq)]
    public async Task<GamePacket[]> OnRoomChgMasterReq(GamePacket request)
    {
        var masterUid = pbCSRoomChgMasterReq.Parser.ParseFrom(request.PkgData).MasterUid;
        var outcome = rooms.ChangeMaster(request.Uid, masterUid);
        logger.LogInformation("Uid {Uid} hands the room to uid {MasterUid}: {Result}", request.Uid, masterUid, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomChgMasterRsp, new pbSCRoomChgMasterRsp { ResultInfo = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomChgPosReq)]
    public async Task<GamePacket[]> OnRoomChgPosReq(GamePacket request)
    {
        var change = pbCSRoomChgPosReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.ChangePos(request.Uid, change.CampClient, change.PosOfCamp);
        logger.LogInformation(
            "Uid {Uid} moves to camp {CampClient} pos {PosOfCamp} (type {ChgType}): {Result}",
            request.Uid,
            change.CampClient,
            change.PosOfCamp,
            change.ChgType,
            outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomChgPosNtf, RoomNotices.ChgPosRefused(outcome.Result))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomChgPosConfirmReq)]
    public async Task<GamePacket[]> OnRoomChgPosConfirmReq(GamePacket request)
    {
        var confirm = pbCSRoomChgPosConfirmReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.ConfirmPosChange(request.Uid, confirm.IsAccept == Accepted, confirm.ChgSeq);
        logger.LogInformation(
            "Uid {Uid} answers position swap {ChgSeq} with {IsAccept}: {Result}",
            request.Uid,
            confirm.ChgSeq,
            confirm.IsAccept,
            outcome.Result);
        return outcome.Succeeded
            ? await DeliverAsync(request, outcome)
            : [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomChgPosNtf, RoomNotices.ChgPosRefused(outcome.Result))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUpdateRoomMapReq)]
    public async Task<GamePacket[]> OnUpdateRoomMapReq(GamePacket request)
    {
        var update = pbCSUpdateRoomMapReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.UpdateUsableMaps(request.Uid, MapList(update.UsableMapInfo));
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateRoomMapRsp, new pbSCUpdateRoomMapRsp { Result = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidInviteFriendJoinRoomReq)]
    public async Task<GamePacket[]> OnInviteFriendJoinRoomReq(GamePacket request)
    {
        var invite = pbCSInviteFriendJoinRoomReq.Parser.ParseFrom(request.PkgData);
        var outcome = await invites.InviteAsync(request.Uid, invite.Uid, invite.InviterType);
        logger.LogInformation(
            "Uid {Uid} invites uid {TargetUid} to the room (type {InviteType}): {Result}",
            request.Uid,
            invite.Uid,
            invite.InviteType,
            outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidInviteFriendJoinRoomRsp, new pbSCInviteFriendJoinRoomRsp
        {
            ResultInfo = Result(outcome),
            Uid = invite.Uid,
        });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidJoinRoomOfFriendReq)]
    public async Task<GamePacket[]> OnJoinRoomOfFriendReq(GamePacket request)
    {
        var join = pbCSJoinRoomOfFriendReq.Parser.ParseFrom(request.PkgData);
        var outcome = await invites.RequestJoinAsync(request.Uid, join.Uid, join.InviterType);
        logger.LogInformation("Uid {Uid} asks to join the room of uid {FriendUid}: {Result}", request.Uid, join.Uid, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidJoinRoomOfFriendRsp, new pbSCJoinRoomOfFriendRsp
        {
            ResultInfo = Result(outcome),
            Uid = join.Uid,
        });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAllowJoinRoomOfFriendReq)]
    public async Task<GamePacket[]> OnAllowJoinRoomOfFriendReq(GamePacket request)
    {
        var allow = pbCSAllowJoinRoomOfFriendReq.Parser.ParseFrom(request.PkgData);
        var outcome = invites.AllowJoin(request.Uid, allow.Uid, allow.InviterType);
        logger.LogInformation("Uid {Uid} lets uid {RequesterUid} join the room: {Result}", request.Uid, allow.Uid, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAllowJoinRoomOfFriendRsp, new pbSCAllowJoinRoomOfFriendRsp
        {
            ResultInfo = Result(outcome),
            Uid = allow.Uid,
        });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidDenyInviteFromFriendReq)]
    public async Task<GamePacket[]> OnDenyInviteFromFriendReq(GamePacket request)
    {
        var deny = pbCSDenyInviteFromFriendReq.Parser.ParseFrom(request.PkgData);
        var outcome = invites.Deny(request.Uid, deny.Uid, deny.DenyIn5Min == DenyForFiveMinutes);
        logger.LogInformation(
            "Uid {Uid} declines the room invite of uid {InviterUid} (five minutes {DenyIn5Min})",
            request.Uid,
            deny.Uid,
            deny.DenyIn5Min);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidDenyInviteFromFriendRsp, new pbSCDenyInviteFromFriendRsp
        {
            ResultInfo = Result(outcome),
            Uid = deny.Uid,
            DenyIn5Min = deny.DenyIn5Min,
        });
        return await DeliverAsync(request, outcome, response);
    }

    private async Task ExpireMatchConfirmAsync(MatchConfirmTicket ticket)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(PveMatch.ConfirmTimeoutMs), clock);
            var outcome = rooms.ExpireMatchConfirm(ticket, out var removedUids);
            if (removedUids.Length == 0)
            {
                return;
            }

            logger.LogInformation(
                "Room {RoomID} match aborted: not every member confirmed within the confirm window",
                ticket.RoomID);
            await PushAllAsync(outcome);
            foreach (var uid in removedUids)
            {
                await MarkIdleAsync(uid);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Match confirm expiry for room {RoomID} failed", ticket.RoomID);
        }
    }

    private async Task<GamePacket[]> CompleteJoinAsync(GamePacket request, RoomOutcome outcome, GamePacket response)
    {
        if (!outcome.Succeeded)
        {
            return [response];
        }

        await MarkInRoomAsync(request.Uid);
        return await DeliverAsync(request, outcome, response);
    }

    private async Task OnRoleOfflineAsync(ulong uid)
    {
        var outcome = rooms.LeaveOnDisconnect(uid);
        if (outcome.Pushes.Count == 0)
        {
            return;
        }

        logger.LogInformation("Uid {Uid} went offline and left the room", uid);
        await PushAllAsync(outcome);
    }

    private Task<GamePacket[]> DeliverAsync(GamePacket request, RoomOutcome outcome, params GamePacket[] leading) =>
        DeliverAsync(request, outcome, leading, []);

    private async Task<GamePacket[]> DeliverAsync(
        GamePacket request,
        RoomOutcome outcome,
        GamePacket[] leading,
        GamePacket[] afterReplies)
    {
        await PushAllAsync(outcome, except: request.Uid);
        var ownPushes = outcome.Pushes
            .Where(push => push.Uid == request.Uid)
            .Select(push => request.Reply(push.Message.MsgID, push.Message.Message))
            .ToArray();
        return
        [
            .. ownPushes.Take(outcome.RequesterPushesBeforeResponse),
            .. leading,
            .. outcome.Replies.Select(reply => request.Reply(reply.MsgID, reply.Message)),
            .. afterReplies,
            .. ownPushes.Skip(outcome.RequesterPushesBeforeResponse),
        ];
    }

    private async Task<RoomMemberProfile> LoadMemberProfileAsync(ulong uid)
    {
        var style = await profiles.LoadAsync(uid);
        var roleSettings = await settings.LoadAsync(uid);
        var channels = await accounts.FindBoundChannelsAsync(uid);
        return new RoomMemberProfile(style.BuildUserPlantInfo(), roleSettings.ABMode, channels.Contains(LoginPlatform.GUEST));
    }

    private async Task PushAllAsync(RoomOutcome outcome, ulong except = 0)
    {
        foreach (var group in outcome.Pushes.Where(push => push.Uid != except).GroupBy(push => push.Uid))
        {
            var packets = group.Select(push => GamePacket.Create(push.Message.MsgID, push.Message.Message)).ToArray();
            if (!await sessions.PushAsync(group.Key, packets))
            {
                logger.LogInformation("Room notices for uid {Uid} were not delivered: the role is offline", group.Key);
            }
        }
    }

    private Task MarkInRoomAsync(ulong uid) =>
        presence.SetGameStateAsync(uid, PB_ENUM_E_ACNT_GAMESTATE.PbEacntGameroom, clock.GetUtcNow().ToUnixTimeSeconds());

    private Task MarkInGameAsync(ulong uid) =>
        presence.SetGameStateAsync(uid, PB_ENUM_E_ACNT_GAMESTATE.PbEacntGamemulti, clock.GetUtcNow().ToUnixTimeSeconds());

    private Task MarkIdleAsync(ulong uid) => presence.SetGameStateAsync(uid, PB_ENUM_E_ACNT_GAMESTATE.PbEacntGameidle, 0);

    private static GamePacket CommErrcode(GamePacket request, PB_ENUM_RES_ERROR_CODE result) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCommErrcodeNtf, new pbSCCommErrcodeNtf
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        });

    private static pbResultInfo Result(RoomOutcome outcome) => new() { ResultId = (int)outcome.Result };

    private static IReadOnlyCollection<int> MapList(pbCOMDT_ROOMMAP_INFO? mapInfo) =>
        mapInfo is null ? [] : mapInfo.MapList;
}
