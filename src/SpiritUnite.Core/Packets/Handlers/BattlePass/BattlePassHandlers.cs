using SpiritUnite.Game.BattlePass;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.BattlePass;

public sealed class BattlePassHandlers(RoleInfoStore roles, BattlePassManager battlePass)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetBattlePassReq)]
    public async Task<GamePacket[]> OnGetBattlePassReq(GamePacket request)
    {
        pbCSGetBattlePassReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetBattlePassRsp, new pbSCGetBattlePassRsp
        {
            ResultInfo = new pbResultInfo(),
            BattlePassInfo = await battlePass.BuildBattlePassInfoAsync(role),
            BPTaskRefreshInfo = BattlePassManager.BuildTaskRefreshInfo(),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassLevelRewardReq)]
    public async Task<GamePacket[]> OnBattlePassLevelRewardReq(GamePacket request)
    {
        var claim = pbCSBattlePassLevelRewardReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await battlePass.ClaimLevelRewardAsync(role, claim.Type, claim.Level);
        return
        [
            .. await BuildLevelRewardPacketsAsync(request, role, outcome),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassLevelRewardRsp, new pbSCBattlePassLevelRewardRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
                Type = claim.Type,
                Level = claim.Level,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetBattlePassLevelRewardBatchReq)]
    public async Task<GamePacket[]> OnGetBattlePassLevelRewardBatchReq(GamePacket request)
    {
        pbCSGetBattlePassLevelRewardBatchReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await battlePass.ClaimAllLevelRewardsAsync(role);
        return
        [
            .. await BuildLevelRewardPacketsAsync(request, role, outcome),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassLevelRewardBatchRsp, new pbSCBattlePassLevelRewardBatchRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassAdditionTreasureReq)]
    public async Task<GamePacket[]> OnBattlePassAdditionTreasureReq(GamePacket request)
    {
        pbCSBattlePassAdditionTreasureReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await battlePass.OpenAdditionTreasureAsync(role);
        return
        [
            .. await BuildChangePacketsAsync(request, role, outcome, PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonBattlepassAddTreasure),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassAdditionTreasureRsp, new pbSCBattlePassAdditionTreasureRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBattlePassBuyLevelReq)]
    public async Task<GamePacket[]> OnBattlePassBuyLevelReq(GamePacket request)
    {
        var purchase = pbCSBattlePassBuyLevelReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await battlePass.BuyLevelAsync(role, purchase.TargetLevel);
        return
        [
            .. await BuildChangePacketsAsync(request, role, outcome, PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonBattlepassBuyLevel),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassBuyLevelRsp, new pbSCBattlePassBuyLevelRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, BattlePassPurchaseMessages.PbCsidBattlePassBuyPassReq)]
    public async Task<GamePacket[]> OnBattlePassBuyPassReq(GamePacket request)
    {
        var purchase = BattlePassPurchaseMessages.ParseReq(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await battlePass.BuyPassAsync(role, purchase.PassType, purchase.ResList);
        return
        [
            .. await BuildChangePacketsAsync(request, role, outcome, PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonBuypass),
            new GamePacket(
                BattlePassPurchaseMessages.PbScidBattlePassBuyPassRsp,
                request.Uid,
                request.ClientSeq,
                BattlePassPurchaseMessages.BuildRsp(
                    new pbResultInfo { ResultId = (int)outcome.Result },
                    outcome.Succeeded ? purchase.PassType : BattlePassSeasons.FreePassType,
                    outcome.Succeeded ? purchase.ResList : [])),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, BattlePassPurchaseMessages.PbCsidBattlePassPackBuyReq)]
    public async Task<GamePacket[]> OnBattlePassPackBuyReq(GamePacket request)
    {
        var purchase = BattlePassPurchaseMessages.ParsePackReq(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await battlePass.BuyLevelPackAsync(role, purchase.PackBuyType);
        return
        [
            .. await BuildChangePacketsAsync(request, role, outcome, PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonBattlepassBuyLevel),
            new GamePacket(
                BattlePassPurchaseMessages.PbScidBattlePassPackBuyRsp,
                request.Uid,
                request.ClientSeq,
                BattlePassPurchaseMessages.BuildPackRsp(new pbResultInfo { ResultId = (int)outcome.Result }, purchase.PackBuyType)),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetBattlePassVideoPlayedReq)]
    public async Task<GamePacket[]> OnSetBattlePassVideoPlayedReq(GamePacket request)
    {
        pbCSSetBattlePassVideoPlayedReq.Parser.ParseFrom(request.PkgData);
        var result = await battlePass.SetVideoPlayedAsync(roles.Require(request.Uid));
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetBattlePassVideoPlayedRsp, new pbSCSetBattlePassVideoPlayedRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRefreshBattlePassTaskReq)]
    public GamePacket[] OnRefreshBattlePassTaskReq(GamePacket request)
    {
        var refresh = pbCSRefreshBattlePassTaskReq.Parser.ParseFrom(request.PkgData);
        var result = battlePass.RefuseTaskRefresh(roles.Require(request.Uid), refresh.TaskId);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRefreshBattlePassTaskRsp, new pbSCRefreshBattlePassTaskRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            TaskId = refresh.TaskId,
            ModuleType = refresh.ModuleType,
        })];
    }

    private async Task<GamePacket[]> BuildLevelRewardPacketsAsync(GamePacket request, LMNRoleInfo role, BattlePassOutcome outcome) =>
        outcome.Commit?.Stored is { } stored
            ?
            [
                .. await BuildChangePacketsAsync(request, role, outcome, PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonBattlepassLevelReward),
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBattlePassLevelRewardInfoNtf, new pbSCBattlePassLevelRewardInfoNtf
                {
                    RewardInfo = battlePass.BuildLevelRewardInfo(stored),
                }),
            ]
            : [];

    private async Task<GamePacket[]> BuildChangePacketsAsync(
        GamePacket request,
        LMNRoleInfo role,
        BattlePassOutcome outcome,
        PB_ENUM_ENM_RESOURCE_CHG_REASON reason) =>
        outcome.Commit is { } commit
            ? [.. (await battlePass.BuildChangePacketsAsync(role, commit, reason)).Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq })]
            : [];
}
