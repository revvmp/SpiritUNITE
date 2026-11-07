using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Auth;
using SpiritUNITE.Battle;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Credit;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Online;

namespace SpiritUNITE.Packets.Handlers.Room;

public sealed class RoomHandlers
{
    private const int Accepted = 1;
    private const int DenyForFiveMinutes = 1;
    private const int IsSubModeCompetition = 1;

    private readonly RoomManager rooms;
    private readonly RoomInvites invites;
    private readonly LobbyRoles lobbyRoles;
    private readonly FriendPresence presence;
    private readonly ProfileManager profiles;
    private readonly SettingsManager settings;
    private readonly AccountManager accounts;
    private readonly RoleInfoStore roles;
    private readonly TmpNameManager tmpNames;
    private readonly ActivitySchedule activitySchedule;
    private readonly ActivityManager activityManager;
    private readonly CreditManager credit;
    private readonly SettleTeamPresence teamPresence;
    private readonly GVoiceOptions gVoice;
    private readonly TimeProvider clock;
    private readonly ILogger<RoomHandlers> logger;

    public RoomHandlers(
        RoomManager rooms,
        RoomInvites invites,
        DeskManager desks,
        RelayManager relays,
        LobbyRoles lobbyRoles,
        FriendPresence presence,
        ProfileManager profiles,
        SettingsManager settings,
        AccountManager accounts,
        RoleInfoStore roles,
        TmpNameManager tmpNames,
        ActivitySchedule activitySchedule,
        ActivityManager activityManager,
        CreditManager credit,
        SettleTeamPresence teamPresence,
        IOptions<GVoiceOptions> gVoice,
        TimeProvider clock,
        ILogger<RoomHandlers> logger)
    {
        this.rooms = rooms;
        this.invites = invites;
        this.lobbyRoles = lobbyRoles;
        this.presence = presence;
        this.profiles = profiles;
        this.settings = settings;
        this.accounts = accounts;
        this.roles = roles;
        this.tmpNames = tmpNames;
        this.activitySchedule = activitySchedule;
        this.activityManager = activityManager;
        this.credit = credit;
        this.teamPresence = teamPresence;
        this.gVoice = gVoice.Value;
        this.clock = clock;
        this.logger = logger;
        lobbyRoles.RoleOffline += OnRoleOfflineAsync;
        desks.DeskClosed += presence.LeaveGameAsync;
        relays.RelayFinished += presence.LeaveGameAsync;
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCreateRoomReq)]
    public async Task<GamePacket[]> OnCreateRoomReq(GamePacket request)
    {
        var create = pbCSCreateRoomReq.Parser.ParseFrom(request.PkgData);
        if (await credit.FindBattleBanEndAsync(request.Uid) is { } banEndTime)
        {
            logger.LogInformation("Create room for uid {Uid} refused: banned from battles until {BanEndTime}", request.Uid, banEndTime);
            return
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomDailyActivityRewardNtf, RoomNotices.DailyActivityReward(await BuildDailyActivityInfoAsync(request.Uid))),
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidJoinRoomNtf, RoomNotices.JoinBanned(banEndTime)),
            ];
        }

        var outcome = rooms.Create(
            request.Uid,
            new RoomCreateRequest(
                create.MapMode,
                create.MapID,
                create.SubMapMode,
                create.FromSystemType,
                MapList(create.UsableMapInfo),
                create.PveChallengeSelectNode,
                create.IsSubModeCompetition == IsSubModeCompetition),
            await LoadMemberProfileAsync(request.Uid));
        logger.LogInformation(
            "Create room for uid {Uid}: MapMode {MapMode}, MapID {MapID}, SubMapMode {SubMapMode}, IsSubModeCompetition {IsSubModeCompetition}, ClientCreateType {ClientCreateType}, {MapCount} usable maps: {Result}",
            request.Uid,
            create.MapMode,
            create.MapID,
            create.SubMapMode,
            create.IsSubModeCompetition,
            create.ClientCreateType,
            create.UsableMapInfo?.MapList.Count ?? 0,
            outcome.Result);
        if (!outcome.Succeeded)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidJoinRoomNtf, RoomNotices.JoinRefused(outcome.Result))];
        }

        await MarkInRoomAsync(request.Uid);
        var dailyActivityInfo = await BuildDailyActivityInfoAsync(request.Uid);
        return await DeliverAsync(
            request,
            outcome,
            [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomDailyActivityRewardNtf, RoomNotices.DailyActivityReward(dailyActivityInfo))],
            [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomRttNtf, RoomNotices.RoomRtt(gVoice, lobbyRoles.Rtt(request.Uid)))]);
    }

    private Task<pbCSDT_ROOM_DAILY_ACTIVITY_INFO> BuildDailyActivityInfoAsync(ulong uid) =>
        RoomDailyActivity.BuildDailyActivityInfoAsync(activitySchedule, activityManager, roles.Require(uid), clock.GetUtcNow());

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

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomSettleOneKeyTeamReq)]
    public async Task<GamePacket[]> OnRoomSettleOneKeyTeamReq(GamePacket request)
    {
        pbCSRoomSettleOneKeyTeamReq.Parser.ParseFrom(request.PkgData);
        var room = rooms.FindSettleRoomOf(request.Uid);
        logger.LogInformation(
            "Uid {Uid} asks to team up again: {Result}",
            request.Uid,
            room is null ? "refused, not in a post-game room" : $"post-game room {room.RoomID}");
        if (room is null)
        {
            return
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomSettleOneKeyTeamRsp, new pbSCRoomSettleOneKeyTeamRsp
                {
                    Result = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrRoomNotFound },
                }),
            ];
        }

        await teamPresence.MarkAsync(request.Uid, SettleTeamPresence.Ready);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomSettleOneKeyTeamRsp, new pbSCRoomSettleOneKeyTeamRsp
            {
                Result = new pbResultInfo(),
                RoomEntity = MatchRoom.RoomEntity,
                RoomID = room.RoomID,
                RoomSeq = room.RoomSeq,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSettleAcntReturnRoomReq)]
    public async Task<GamePacket[]> OnSettleAcntReturnRoomReq(GamePacket request)
    {
        pbCSSettleAcntReturnRoomReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.ReturnFromSettle(request.Uid);
        logger.LogInformation("Uid {Uid} returns from the settlement to its room: {Result}", request.Uid, outcome.Result);
        if (!outcome.Succeeded)
        {
            return
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSettleAcntReturnRoomNtf, new pbSCSettleAcntReturnRoomNtf
                {
                    ResultInfo = Result(outcome),
                    Uid = request.Uid,
                }),
            ];
        }

        await teamPresence.MarkAsync(request.Uid, SettleTeamPresence.Offline);
        await MarkInRoomAsync(request.Uid);
        return await DeliverAsync(request, outcome);
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

        if (targetUid is { } kickedUid)
        {
            await MarkIdleAsync(kickedUid);
        }

        return await DeliverAsync(request, outcome);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAddRoomAireq)]
    public async Task<GamePacket[]> OnAddRoomAIReq(GamePacket request)
    {
        var campClient = pbCSAddRoomAIReq.Parser.ParseFrom(request.PkgData).CampClient;
        var outcome = rooms.AddRoomAI(request.Uid, campClient);
        logger.LogInformation("Uid {Uid} adds a CPU Trainer to camp {CampClient}: {Result}", request.Uid, campClient, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAddRoomAirsp, new pbSCAddRoomAIRsp { Result = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUpdateFriendlyRoomRuleReq)]
    public async Task<GamePacket[]> OnUpdateFriendlyRoomRuleReq(GamePacket request)
    {
        var ruleList = pbCSUpdateFriendlyRoomRuleReq.Parser.ParseFrom(request.PkgData).RoomRuleInfo?.RuleList.ToArray() ?? [];
        var outcome = rooms.UpdateFriendlyRoomRule(request.Uid, ruleList);
        logger.LogInformation(
            "Uid {Uid} sets the custom room rules to {RuleList}: {Result}",
            request.Uid,
            string.Join(',', ruleList),
            outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateFriendlyRoomRuleRsp, new pbSCUpdateFriendlyRoomRuleRsp { Result = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUpdateRoomGlobalBanListReq)]
    public async Task<GamePacket[]> OnUpdateRoomGlobalBanListReq(GamePacket request)
    {
        var banPokemonList = pbCSUpdateRoomGlobalBanListReq.Parser.ParseFrom(request.PkgData).RoomGlobalBanList?.BanPokemonList.ToArray() ?? [];
        var outcome = rooms.UpdateRoomGlobalBanList(request.Uid, banPokemonList);
        logger.LogInformation(
            "Uid {Uid} sets the room global ban list to {BanPokemonList}: {Result}",
            request.Uid,
            string.Join(',', banPokemonList),
            outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateRoomGlobalBanListRsp, new pbSCUpdateRoomGlobalBanListRsp
        {
            Result = Result(outcome),
            RoomGlobalBanList = new pbCOMDT_ROOM_GLOBAL_BAN_POKEMON_LIST { BanPokemonList = { rooms.FindRoomGlobalBanList(request.Uid) ?? [] } },
        });
        return await DeliverAsync(request, outcome, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRoomGlobalBanListReq)]
    public GamePacket[] OnGetRoomGlobalBanListReq(GamePacket request)
    {
        var banPokemonList = rooms.FindRoomGlobalBanList(request.Uid);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRoomGlobalBanListRsp, new pbSCGetRoomGlobalBanListRsp
            {
                Result = new pbResultInfo
                {
                    ResultId = (int)(banPokemonList is null ? PB_ENUM_RES_ERROR_CODE.PbErrRoomAcntNotInRoom : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc),
                },
                RoomGlobalBanList = new pbCOMDT_ROOM_GLOBAL_BAN_POKEMON_LIST { BanPokemonList = { banPokemonList ?? [] } },
            }),
        ];
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
        var outcome = await invites.InviteAsync(request.Uid, invite.Uid, invite.InviteType, invite.InviterType);
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

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChgRoleTmpNameReq)]
    public async Task<GamePacket[]> OnChgRoleTmpNameReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var chg = pbCSChgRoleTmpNameReq.Parser.ParseFrom(request.PkgData);
        var result = await tmpNames.ChgRoleTmpNameAsync(role.GameUlongUID, chg);
        logger.LogInformation(
            "Temporary name of type {TmpNameType} for uid {Uid}: {Result}",
            chg.TmpNameType,
            role.GameUlongUID,
            result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChgRoleTmpNameRsp, new pbSCChgRoleTmpNameRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        });
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return [response];
        }

        var outcome = rooms.ChangeTmpNameList(role.GameUlongUID, await tmpNames.BuildTmpNameListAsync(role.GameUlongUID));
        return await DeliverAsync(request, outcome, response);
    }

    private async Task<GamePacket[]> CompleteJoinAsync(GamePacket request, RoomOutcome outcome, GamePacket response)
    {
        if (!outcome.Succeeded)
        {
            return [response];
        }

        await MarkInRoomAsync(request.Uid);
        var dailyActivityInfo = await BuildDailyActivityInfoAsync(request.Uid);
        return await DeliverAsync(
            request,
            outcome,
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomDailyActivityRewardNtf, RoomNotices.DailyActivityReward(dailyActivityInfo)),
            response);
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
        return new RoomMemberProfile(
            style.BuildUserPlantInfo(),
            roleSettings.ABMode,
            channels.Contains(LoginPlatform.GUEST),
            await tmpNames.BuildTmpNameListAsync(uid));
    }

    private async Task PushAllAsync(RoomOutcome outcome, ulong except = 0)
    {
        foreach (var group in outcome.Pushes.Where(push => push.Uid != except).GroupBy(push => push.Uid))
        {
            var packets = group.Select(push => GamePacket.Create(push.Message.MsgID, push.Message.Message)).ToArray();
            if (!await lobbyRoles.PushAsync(group.Key, packets))
            {
                logger.LogInformation("Room notices for uid {Uid} were not delivered: the role is offline", group.Key);
            }
        }
    }

    private Task MarkInRoomAsync(ulong uid) =>
        presence.SetGameStateAsync(uid, PB_ENUM_E_ACNT_GAMESTATE.PbEacntGameroom, clock.GetUtcNow().ToUnixTimeSeconds());

    private Task MarkIdleAsync(ulong uid) => presence.SetGameStateAsync(uid, PB_ENUM_E_ACNT_GAMESTATE.PbEacntGameidle, 0);

    private static GamePacket CommErrcode(GamePacket request, PB_ENUM_RES_ERROR_CODE result) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCommErrcodeNtf, new pbSCCommErrcodeNtf
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        });

    private static pbResultInfo Result(RoomOutcome outcome) => new() { ResultId = (int)outcome.Result, Args = outcome.ResultArgs };

    private static IReadOnlyCollection<int> MapList(pbCOMDT_ROOMMAP_INFO? mapInfo) =>
        mapInfo is null ? [] : mapInfo.MapList;
}
