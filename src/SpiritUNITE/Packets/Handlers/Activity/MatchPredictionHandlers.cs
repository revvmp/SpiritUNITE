using SpiritUNITE.Game.Activity;
using SpiritUNITE.Packets.Messages;
using static SpiritUNITE.Packets.Messages.PB_ENUM_CS_MSGID_DEF;

namespace SpiritUNITE.Packets.Handlers.Activity;

public sealed class MatchPredictionHandlers(MatchPredictionManager matchPrediction)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidMatchPredictionStatusReq)]
    public GamePacket[] OnMatchPredictionStatusReq(GamePacket request)
    {
        pbCSMatchPredictionStatusReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidMatchPredictionStatusRsp, matchPrediction.Status())];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidMatchPredictionSelectReq)]
    public GamePacket[] OnMatchPredictionSelectReq(GamePacket request)
    {
        var select = pbCSMatchPredictionSelectReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidMatchPredictionSelectRsp, matchPrediction.Select(request.Uid, select.WcsTeamList))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidMatchPredictionTeamInfoReq)]
    public GamePacket[] OnMatchPredictionTeamInfoReq(GamePacket request)
    {
        var teamInfo = pbCSMatchPredictionTeamInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidMatchPredictionTeamInfoRsp, matchPrediction.TeamInfo(teamInfo.WcsTeamID))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidMatchPredictionRewardReq)]
    public GamePacket[] OnMatchPredictionRewardReq(GamePacket request)
    {
        pbCSMatchPredictionRewardReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidMatchPredictionRewardRsp, matchPrediction.Reward(request.Uid))];
    }
}
