using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Activity;

public sealed class BossRushHandlers
{
    private const int NoActivePeriodID = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRefreshBossRushReq)]
    public GamePacket[] OnRefreshBossRushReq(GamePacket request)
    {
        pbCSRefreshBossRushReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCBossRushPeriodNtf { PeriodID = NoActivePeriodID };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBossRushPeriodNtf, response)];
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
}
