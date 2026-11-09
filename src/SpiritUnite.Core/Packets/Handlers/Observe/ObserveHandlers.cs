using Microsoft.Extensions.Logging;
using SpiritUnite.Battle;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Observe;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Observe;

public sealed class ObserveHandlers(
    RoleInfoStore roles,
    ObserveBattleManager observeBattles,
    ObserveManager observe,
    ActivityManager activityManager,
    ActivityProgressPushes activityProgressPushes,
    ILogger<ObserveHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidFriendBattleListReq)]
    public async Task<GamePacket[]> OnFriendBattleListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSFriendBattleListReq.Parser.ParseFrom(request.PkgData);
        var battles = await observeBattles.BuildFriendBattleListAsync(request.Uid, observe.FindObservableBattles(request.Uid));
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidFriendBattleListRsp, new pbSCFriendBattleListRsp { ResultInfo = new pbResultInfo(), BattleList = { battles } })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGreatBattleListReq)]
    public async Task<GamePacket[]> OnGreatBattleListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGreatBattleListReq.Parser.ParseFrom(request.PkgData);
        var battles = await observeBattles.BuildGreatBattleListAsync(observe.FindObservableBattles(request.Uid));
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGreatBattleListRsp, new pbSCGreatBattleListRsp { ResultInfo = new pbResultInfo(), BattleList = { battles } })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidObserveFriendBattleReq)]
    public async Task<GamePacket[]> OnObserveFriendBattleReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var friendUid = pbCSObserveFriendBattleReq.Parser.ParseFrom(request.PkgData).FriendUID;
        var choice = observe.IsOnRelayDesk(request.Uid)
            ? ObserveChoice.Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveNotInIdleState)
            : await observeBattles.ChooseFriendBattleAsync(request.Uid, friendUid, observe.FindObservableBattles(request.Uid));
        var tgwinfo = choice.Battle is { } battle ? observe.WatchThroughRelay(request.Uid, battle) : null;
        if (choice.Battle is not null && tgwinfo is null)
        {
            choice = ObserveChoice.Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveVideoNotExist);
        }

        var response = new pbSCObserveFriendBattleRsp { ResultInfo = new pbResultInfo { ResultId = (int)choice.Result } };
        if (choice.Battle is { } chosen && tgwinfo is not null)
        {
            response.Tgwinfo = tgwinfo;
            response.FriendUID = friendUid;
            response.Desk = chosen.BuildOBDesk();
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidObserveFriendBattleRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidObserveGreatBattleReq)]
    public async Task<GamePacket[]> OnObserveGreatBattleReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var desk = pbCSObserveGreatBattleReq.Parser.ParseFrom(request.PkgData).Desk;
        var choice = observe.IsOnRelayDesk(request.Uid)
            ? ObserveChoice.Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveNotInIdleState)
            : await observeBattles.ChooseGreatBattleAsync(desk, observe.FindObservableBattles(request.Uid));
        if (choice.Battle is { } battle && !observe.WatchOnLobby(request.Uid, battle))
        {
            choice = ObserveChoice.Refused(PB_ENUM_RES_ERROR_CODE.PbErrObserveVideoNotExist);
        }

        var response = new pbSCObserveGreatBattleRsp { ResultInfo = new pbResultInfo { ResultId = (int)choice.Result } };
        if (choice.Battle is { } chosen)
        {
            response.Desk = chosen.BuildOBDesk();
            response.ObserveUID = choice.ObserveUID;
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidObserveGreatBattleRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetVideoOfFrapsReq)]
    public GamePacket[] OnGetVideoOfFrapsReq(GamePacket request) => ReplyVideoOfFraps(request);

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetVideoOfFrapsReq)]
    public GamePacket[] OnRelayGetVideoOfFrapsReq(GamePacket request) => ReplyVideoOfFraps(request);

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQuitObserveReq)]
    public async Task<GamePacket[]> OnQuitObserveReq(GamePacket request)
    {
        var quit = pbCSQuitObserveReq.Parser.ParseFrom(request.PkgData);
        var reply = new ReplyList(request);
        reply.Add(PB_ENUM_CS_MSGID_DEF.PbScidQuitObserveRsp, new pbSCQuitObserveRsp { ResultInfo = new pbResultInfo() });
        if (observe.Quit(request.Uid) != PB_ENUM_ENM_OBSERVE_STATE.PbEnmObserveFriend || roles.Find(request.Uid) is not { } role)
        {
            return reply.ToArray();
        }

        var progress = await activityManager.RecordClientActionAsync(role, ActivityClientActions.ObservedFriendBattle);
        logger.LogInformation("Friend battle watch by uid {Uid} ended after {ObserveTime} s: {TaskCount} tasks progressed",
            request.Uid, quit.ObserveTime, progress.UpdatedTasks.Length);
        reply.AddRange(await activityProgressPushes.BuildAsync(role, progress));
        return reply.ToArray();
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetObservePrivacyReq)]
    public GamePacket[] OnSetObservePrivacyReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var notShowInList = pbCSSetObservePrivacyReq.Parser.ParseFrom(request.PkgData).NotShowInList;
        var applied = observeBattles.SetObservePrivacy(request.Uid, notShowInList);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetObservePrivacyRsp, new pbSCSetObservePrivacyRsp { ResultInfo = new pbResultInfo(), NotShowInList = applied })];
    }

    private GamePacket[] ReplyVideoOfFraps(GamePacket request) =>
        [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetVideoOfFrapsRsp, observe.GetVideoOfFraps(request.Uid, pbCSGetVideoOfFrapsReq.Parser.ParseFrom(request.PkgData)))];
}
