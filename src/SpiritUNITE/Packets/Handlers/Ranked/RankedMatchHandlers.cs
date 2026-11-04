using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Ranked;

public sealed class RankedMatchHandlers(GameData gameData, TimeProvider clock)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchInfoReq)]
    public GamePacket[] OnGetRankedMatchInfoReq(GamePacket request)
    {
        pbCSGetRankedMatchInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetRankedMatchInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            Detail = RankedMatch.BuildStartingDetail(gameData, clock.GetUtcNow()),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchInfoRsp, response)];
    }
}
