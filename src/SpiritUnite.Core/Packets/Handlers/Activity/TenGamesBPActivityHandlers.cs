using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;
using static SpiritUnite.Packets.Messages.PB_ENUM_CS_MSGID_DEF;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class TenGamesBPActivityHandlers(
    TenGamesBPActivityManager bpActivity,
    RoleInfoStore roles,
    ActivityRewardNotices rewardNotices)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidTenGamesBpactivityInfoReq)]
    public async Task<GamePacket[]> OnTenGamesBPActivityInfoReq(GamePacket request)
    {
        var info = pbCSTenGamesBPActivityInfoReq.Parser.ParseFrom(request.PkgData);
        var outcome = await bpActivity.InfoAsync(request.Uid, info.Reserved);
        return
        [
            request.Reply(PbScidTenGamesBpactivityInfoRsp, new pbSCTenGamesBPActivityInfoRsp
            {
                ResultInfo = Result(outcome),
                BattlePassActivityInfo = outcome.Info,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidTenGamesBpactivityBuySenierRewardReq)]
    public async Task<GamePacket[]> OnTenGamesBPActivityBuySenierRewardReq(GamePacket request)
    {
        var buy = pbCSTenGamesBPActivityBuySenierRewardReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await bpActivity.BuySenierRewardAsync(role, buy.ActivityID);
        var response = request.Reply(PbScidTenGamesBpactivityBuySenierRewardRsp, new pbSCTenGamesBPActivityBuySenierRewardRsp
        {
            ResultInfo = Result(outcome),
            BattlePassActivityInfo = outcome.Info,
        });
        return [.. await NoticesAsync(role, request, outcome), response];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidTenGamesBpactivityGetRewardReq)]
    public async Task<GamePacket[]> OnTenGamesBPActivityGetRewardReq(GamePacket request)
    {
        var claim = pbCSTenGamesBPActivityGetRewardReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await bpActivity.GetRewardAsync(role, claim.ActivityID, claim.RewardSeq, claim.ReceiveRewardType);
        var response = request.Reply(PbScidTenGamesBpactivityGetRewardRsp, new pbSCTenGamesBPActivityGetRewardRsp
        {
            ResultInfo = Result(outcome),
            BattlePassActivityInfo = outcome.Info,
        });
        if (outcome.Rewards is not { Awards.Count: > 0 } rewards)
        {
            return [.. await NoticesAsync(role, request, outcome), response];
        }

        return
        [
            .. await NoticesAsync(role, request, outcome),
            request.Reply(PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTenGameBpActivityReward,
                ResourcesList = { rewards.Awards },
            }),
            response,
        ];
    }

    private async Task<IEnumerable<GamePacket>> NoticesAsync(LMNRoleInfo role, GamePacket request, TenGamesBPOutcome outcome) =>
        (await rewardNotices.BuildAsync(role, outcome.Rewards)).Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq });

    private static pbResultInfo Result(TenGamesBPOutcome outcome) => new() { ResultId = (int)outcome.Result };
}
