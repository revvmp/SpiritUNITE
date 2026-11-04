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
}
