using SpiritUnite.Game.Guild;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Guild;

public sealed class BossRushHandlers(BossRushGuildActivity bossRush)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRefreshBossRushReq)]
    public GamePacket[] OnRefreshBossRushReq(GamePacket request)
    {
        pbCSRefreshBossRushReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBossRushPeriodNtf, bossRush.BuildPeriodNtf())];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryBossRushGuildActivityInfoReq)]
    public GamePacket[] OnQueryBossRushGuildActivityInfoReq(GamePacket request)
    {
        pbCSQueryBossRushGuildActivityInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCQueryBossRushGuildActivityInfoRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryBossRushGuildActivityInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetBossRushGuildScoreRewardReq)]
    public async Task<GamePacket[]> OnGetBossRushGuildScoreRewardReq(GamePacket request)
    {
        var rewardGear = pbCSGetBossRushGuildScoreRewardReq.Parser.ParseFrom(request.PkgData).RewardGear;
        var result = await bossRush.ClaimScoreRewardAsync(request.Uid, rewardGear);
        var response = new pbSCGetBossRushGuildScoreRewardRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetBossRushGuildScoreRewardRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidQueryBossRushPassAwardReq)]
    public GamePacket[] OnQueryBossRushPassAwardReq(GamePacket request)
    {
        pbCSQueryBossRushPassAwardReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQueryBossRushPassAwardRsp, bossRush.BuildPassAwardRsp())];
    }
}
