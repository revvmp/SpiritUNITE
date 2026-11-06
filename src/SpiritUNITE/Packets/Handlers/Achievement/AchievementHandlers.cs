using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Achievement;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Achievement;

public sealed class AchievementHandlers(
    RoleInfoStore roles,
    AchievementManager achievementManager,
    ILogger<AchievementHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAchievementInfoReq)]
    public async Task<GamePacket[]> OnGetAchievementInfoReq(GamePacket request)
    {
        pbCSGetAchievementInfoReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAchievementInfoRsp, new pbSCGetAchievementInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            Score = await achievementManager.GetScoreAsync(role.GameUlongUID),
            AchievementInfo = AchievementScoreReward.BuildDetailList(),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAchievementScoreRewardGetReq)]
    public async Task<GamePacket[]> OnAchievementScoreRewardGetReq(GamePacket request)
    {
        var claim = pbCSAchievementScoreRewardGetReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var result = AchievementScoreReward.RefuseClaim(claim.Score, await achievementManager.GetScoreAsync(role.GameUlongUID));
        logger.LogInformation(
            "Achievement score reward {Score} refused for uid {Uid}: {Result}; the score reward contents are not in the client tables",
            claim.Score, request.Uid, result);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAchievementScoreRewardGetRsp, new pbSCAchievementScoreRewardGetRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            AchievementInfo = AchievementScoreReward.BuildDetailList(),
        })];
    }
}
