using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.PveChallenge;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.PveChallenge;

public sealed class PveChallengeHandlers(
    RoleInfoStore roles,
    PveChallengeProgress pveChallenge,
    PveChallengeManager pveChallengeManager,
    TimeProvider clock,
    ILogger<PveChallengeHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPveChallengeStageInfoReq)]
    public async Task<GamePacket[]> OnPveChallengeStageInfoReq(GamePacket request)
    {
        var stageID = pbCSPveChallengeStageInfoReq.Parser.ParseFrom(request.PkgData).StageID;
        var role = roles.Require(request.Uid);
        var nodes = pveChallenge.Load(role);
        var now = clock.GetUtcNow();
        var shopState = await pveChallengeManager.LoadShopStateAsync(role.GameUlongUID);
        var week = await pveChallengeManager.LoadWeekAsync(role.GameUlongUID, now);
        var response = new pbSCPveChallengeStageInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            PveChallengeInfo = PveChallengeProgress.BuildInfo(nodes),
            PveChallengeBuffInfo = pveChallengeManager.BuildBuffInfo(shopState),
            PveChallengeShopInfo = PveChallengeManager.BuildShopInfo(shopState),
            ReqStageID = stageID,
            WeekRewardGetNum = week.WeekRewardGetNum,
            PrivilegeInfo = await pveChallengeManager.BuildVisiblePrivilegeInfoAsync(role.GameUlongUID, now),
        };
        return
        [
            .. PveChallengeProgress.BuildNewbieCompletedNodeNtfs(role, nodes)
                .Select(ntf => request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeStageInfoNtf, ntf)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeStageInfoRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPveChallengeShopBuyReq)]
    public async Task<GamePacket[]> OnPveChallengeShopBuyReq(GamePacket request)
    {
        var buy = pbCSPveChallengeShopBuyReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await pveChallengeManager.BuyAsync(role, buy.ShopId, buy.ShopCnt);
        LogOutcome("PvE challenge shop buy", request.Uid, $"shop {buy.ShopId} x{buy.ShopCnt}", outcome.Result);
        var response = new pbSCPveChallengeShopBuyRsp { ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result } };
        if (outcome.State is { } state)
        {
            response.PveChallengeBuffInfo = pveChallengeManager.BuildBuffInfo(state);
            response.PveChallengeShopInfo = PveChallengeManager.BuildShopInfo(state);
        }

        return [.. AsReplies(request, outcome.BeforeRsp), request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeShopBuyRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPveChallengeBuffClearNewTagReq)]
    public async Task<GamePacket[]> OnPveChallengeBuffClearNewTagReq(GamePacket request)
    {
        var buffID = pbCSPveChallengeBuffClearNewTagReq.Parser.ParseFrom(request.PkgData).BuffID;
        var role = roles.Require(request.Uid);
        var result = await pveChallengeManager.ClearBuffNewTagAsync(role, buffID);
        LogOutcome("PvE challenge buff new tag clear", request.Uid, $"buff {buffID}", result);
        var response = new pbSCPveChallengeBuffClearNewTagRsp { ResultInfo = new pbResultInfo { ResultId = (int)result }, BuffID = buffID };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeBuffClearNewTagRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidActivatePvePrivilegeReq)]
    public async Task<GamePacket[]> OnActivatePvePrivilegeReq(GamePacket request)
    {
        var activityID = pbCSActivatePvePrivilegeReq.Parser.ParseFrom(request.PkgData).ActivityID;
        var role = roles.Require(request.Uid);
        var outcome = await pveChallengeManager.ActivatePrivilegeAsync(role, activityID);
        LogOutcome("PvE privilege activation", request.Uid, $"activity {activityID}", outcome.Result);
        var response = new pbSCActivatePvePrivilegeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ActivityID = activityID,
            ActivityInfo = outcome.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
                ? new pbCOMDT_PVE_PRIVILEGE_ACTIVITY_INFO { PrivilegeExpireTime = outcome.PrivilegeExpireTime }
                : await pveChallengeManager.BuildPrivilegeInfoAsync(role.GameUlongUID, activityID),
        };
        return [.. AsReplies(request, outcome.BeforeRsp), request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidActivatePvePrivilegeRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUsePveSettleBonusCardReq)]
    public async Task<GamePacket[]> OnUsePveSettleBonusCardReq(GamePacket request)
    {
        var use = pbCSUsePveSettleBonusCardReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await pveChallengeManager.UseSettleBonusCardAsync(role, new PveChallengeNodeKey(use.StageID, use.NodeID, use.DifficultyLevel));
        LogOutcome("PvE settle bonus card", request.Uid, $"node {use.StageID}-{use.NodeID}-{use.DifficultyLevel}", outcome.Result);
        var response = new pbSCUsePveSettleBonusCardRsp { ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result } };
        return [.. AsReplies(request, outcome.BeforeRsp), request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUsePveSettleBonusCardRsp, response)];
    }

    private static IEnumerable<GamePacket> AsReplies(GamePacket request, IEnumerable<GamePacket> packets) =>
        packets.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq });

    private void LogOutcome(string operation, ulong uid, string target, PB_ENUM_RES_ERROR_CODE result) =>
        logger.LogInformation("{Operation} of {Target} for uid {Uid}: {Result}", operation, target, uid, result);
}
