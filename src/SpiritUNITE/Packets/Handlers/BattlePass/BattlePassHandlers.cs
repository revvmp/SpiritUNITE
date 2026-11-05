using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.BattlePass;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.BattlePass;

public sealed class BattlePassHandlers(
    RoleInfoStore roles,
    GameData gameData,
    TimeProvider clock,
    ILogger<BattlePassHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetBattlePassReq)]
    public GamePacket[] OnGetBattlePassReq(GamePacket request)
    {
        pbCSGetBattlePassReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var now = clock.GetUtcNow();
        var current = BattlePassSeasons.FindCurrentSeason(gameData, now);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetBattlePassRsp, new pbSCGetBattlePassRsp
        {
            ResultInfo = new pbResultInfo(),
            BattlePassInfo = BattlePassSeasons.BuildStartingInfo(current, now),
            BPTaskRefreshInfo = new pbCSDT_BATTLE_PASS_TASK_REFRESH_INFO(),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassLevelRewardReq)]
    public GamePacket[] OnBattlePassLevelRewardReq(GamePacket request)
    {
        var claim = pbCSBattlePassLevelRewardReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var result = RefuseLevelReward(request.Uid, "level reward");
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassLevelRewardRsp, new pbSCBattlePassLevelRewardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            Type = claim.Type,
            Level = claim.Level,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetBattlePassLevelRewardBatchReq)]
    public GamePacket[] OnGetBattlePassLevelRewardBatchReq(GamePacket request)
    {
        pbCSGetBattlePassLevelRewardBatchReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var result = RefuseLevelReward(request.Uid, "batch level reward");
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassLevelRewardBatchRsp, new pbSCBattlePassLevelRewardBatchRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassAdditionTreasureReq)]
    public GamePacket[] OnBattlePassAdditionTreasureReq(GamePacket request)
    {
        pbCSBattlePassAdditionTreasureReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var result = Refuse(request.Uid, "addition treasure", PB_ENUM_RES_ERROR_CODE.PbErrBattlePassAdditionTreasureNoReward);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassAdditionTreasureRsp, new pbSCBattlePassAdditionTreasureRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassBuyLevelReq)]
    public GamePacket[] OnBattlePassBuyLevelReq(GamePacket request)
    {
        var purchase = pbCSBattlePassBuyLevelReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var result = purchase.TargetLevel <= BattlePassSeasons.StartingLevel
            ? PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidTargetLevel
            : Refuse(request.Uid, "level purchase", PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidTargetLevel);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassBuyLevelRsp, new pbSCBattlePassBuyLevelRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetBattlePassVideoPlayedReq)]
    public GamePacket[] OnSetBattlePassVideoPlayedReq(GamePacket request)
    {
        pbCSSetBattlePassVideoPlayedReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var result = Refuse(request.Uid, "video played flag", PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetBattlePassVideoPlayedRsp, new pbSCSetBattlePassVideoPlayedRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRefreshBattlePassTaskReq)]
    public GamePacket[] OnRefreshBattlePassTaskReq(GamePacket request)
    {
        var refresh = pbCSRefreshBattlePassTaskReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var result = Refuse(request.Uid, $"task {refresh.TaskId} refresh", PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRefreshBattlePassTaskRsp, new pbSCRefreshBattlePassTaskRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            TaskId = refresh.TaskId,
            ModuleType = refresh.ModuleType,
        })];
    }

    private PB_ENUM_RES_ERROR_CODE RefuseLevelReward(ulong uid, string action) =>
        Refuse(uid, action, PB_ENUM_RES_ERROR_CODE.PbErrBattlePassBatchGetLevelRewardNoReward);

    private PB_ENUM_RES_ERROR_CODE Refuse(ulong uid, string action, PB_ENUM_RES_ERROR_CODE inSeasonResult)
    {
        var current = BattlePassSeasons.FindCurrentSeason(gameData, clock.GetUtcNow());
        var result = current.IsInProcess ? inSeasonResult : PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState;
        logger.LogInformation(
            "Battle pass {Action} from uid {Uid} answered {Result}: season {SeasonID} is {SeasonState} and battle pass progress is not tracked",
            action, uid, current.Season.SeasonID, current.SeasonState, result);
        return result;
    }
}
