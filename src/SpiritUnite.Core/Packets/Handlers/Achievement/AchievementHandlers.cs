using SpiritUnite.Game.Achievement;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Achievement;

public sealed class AchievementHandlers(
    RoleInfoStore roles,
    AchievementManager achievementManager,
    AchievementScoreRewardManager scoreRewards)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAchievementInfoReq)]
    public async Task<GamePacket[]> OnGetAchievementInfoReq(GamePacket request)
    {
        pbCSGetAchievementInfoReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var score = await achievementManager.GetScoreAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAchievementInfoRsp, new pbSCGetAchievementInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            Score = score,
            AchievementInfo = await scoreRewards.BuildDetailListAsync(role.GameUlongUID, score),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAchievementScoreRewardGetReq)]
    public async Task<GamePacket[]> OnAchievementScoreRewardGetReq(GamePacket request)
    {
        var claimRequest = pbCSAchievementScoreRewardGetReq.Parser.ParseFrom(request.PkgData);
        var claim = await scoreRewards.ClaimAsync(roles.Require(request.Uid), claimRequest.Score);
        return
        [
            .. claim.BeforeRsp.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAchievementScoreRewardGetRsp, new pbSCAchievementScoreRewardGetRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)claim.Result },
                AchievementInfo = claim.AchievementInfo,
            }),
        ];
    }
}
