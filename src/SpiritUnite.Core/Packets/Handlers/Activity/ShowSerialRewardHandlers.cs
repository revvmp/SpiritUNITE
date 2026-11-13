using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class ShowSerialRewardHandlers(
    RoleInfoStore roles,
    ShowSerialRewardManager showSerialRewards,
    ActivityRewardNotices rewardNotices)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShowSerialRewardActivityStatusReq)]
    public async Task<GamePacket[]> OnShowSerialRewardActivityStatusReq(GamePacket request)
    {
        var statusRequest = pbCSShowSerialRewardActivityStatusReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var (result, activityInfo) = await showSerialRewards.BuildStatusAsync(role, statusRequest.ActivityID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidShowSerialRewardActivityStatusRsp, new pbSCShowSerialRewardActivityStatusRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            ActivityID = statusRequest.ActivityID,
            ActivityInfo = activityInfo,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShowSerialRewardActivityGetRewardReq)]
    public async Task<GamePacket[]> OnShowSerialRewardActivityGetRewardReq(GamePacket request)
    {
        var rewardRequest = pbCSShowSerialRewardActivityGetRewardReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var claim = await showSerialRewards.ClaimAsync(role, rewardRequest.ActivityID, rewardRequest.RewardSeq);
        var reply = new ReplyList(request);
        if (claim.Rewards is { } rewards)
        {
            reply.AddRange(await rewardNotices.BuildAsync(role, rewards));
            reply.Add(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonShowSerialRewardActivityReward,
                ResourcesList = { rewards.Awards },
            });
        }

        reply.Add(PB_ENUM_CS_MSGID_DEF.PbScidShowSerialRewardActivityGetRewardRsp, new pbSCShowSerialRewardActivityGetRewardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)claim.Result },
            ActivityID = rewardRequest.ActivityID,
            ActivityInfo = claim.ActivityInfo,
        });
        return reply.ToArray();
    }
}
