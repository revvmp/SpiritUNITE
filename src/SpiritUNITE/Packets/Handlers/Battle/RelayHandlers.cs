using SpiritUNITE.Battle;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Battle;

public sealed class RelayHandlers(RelayManager relays)
{
    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidMultgameLoadprocessReq)]
    public async Task<GamePacket[]> OnMultgameLoadprocessReq(GamePacket request)
    {
        await relays.LoadProcessAsync(request.Uid, pbCSMultgameLoadprocessReq.Parser.ParseFrom(request.PkgData));
        return [];
    }

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidMultgameLoadfinReq)]
    public GamePacket[] OnMultgameLoadfinReq(GamePacket request)
    {
        pbCSMultgameLoadfinReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMultgameLoadfinRsp, relays.LoadFinished(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidMutilGameLoadFinishInfoReq)]
    public GamePacket[] OnMutilGameLoadFinishInfoReq(GamePacket request)
    {
        pbCSMutilGameLoadFinishInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMutilGameLoadFinishInfoRsp, new pbSCMutilGameLoadFinishInfoRsp())];
    }

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidReConnGameReq)]
    public GamePacket[] OnReConnGameReq(GamePacket request)
    {
        pbCSReConnGameReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidReConnGameRsp, relays.ReConnGame(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidRelaysvrPingNtf)]
    public async Task<GamePacket[]> OnRelaysvrPingNtf(GamePacket request)
    {
        pbCSRelaysvrPingNtf.Parser.ParseFrom(request.PkgData);
        await relays.PingAsync(request.Uid, request);
        return [];
    }

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidMultGameAliveStateReq)]
    public GamePacket[] OnMultGameAliveStateReq(GamePacket request) =>
        [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMultGameAliveStateRsp, relays.AliveState(request.Uid, pbCSMultGameAliveStateReq.Parser.ParseFrom(request.PkgData)))];

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidOpeningGameHangUpReq)]
    public GamePacket[] OnOpeningGameHangUpReq(GamePacket request)
    {
        relays.HangUp(request.Uid, pbCSOpeningGameHangUpReq.Parser.ParseFrom(request.PkgData));
        return [];
    }

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidRelayHashcheckReq)]
    public GamePacket[] OnRelayHashcheckReq(GamePacket request)
    {
        relays.HashCheck(request.Uid, pbCSRelayHashcheckReq.Parser.ParseFrom(request.PkgData));
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRelayConnectionProblemReport)]
    public GamePacket[] OnRelayConnectionProblemReport(GamePacket request)
    {
        relays.ReportConnectionProblem(request.Uid, pbCSRelayConnectionProblemReport.Parser.ParseFrom(request.PkgData));
        return [];
    }

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidGamingUpermsgReq)]
    public GamePacket[] OnGamingUpermsgReq(GamePacket request)
    {
        relays.Upermsg(request.Uid, pbCSGamingUpermsgReq.Parser.ParseFrom(request.PkgData));
        return [];
    }

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidFrapBootRepairReq)]
    public GamePacket[] OnFrapBootRepairReq(GamePacket request) =>
        [request.Reply(PB_ENUM_CS_MSGID_DEF.PbCsidFrapBootRepairRsp, relays.RepairFrames(request.Uid, pbCSFrapBootRepairReq.Parser.ParseFrom(request.PkgData)))];

    [PacketHandler(eNetEventServerType.RelayServer, PB_ENUM_CS_MSGID_DEF.PbCsidMultgameGameoverReq)]
    public GamePacket[] OnMultgameGameoverReq(GamePacket request) =>
        [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMultgameGameoverRsp, relays.Gameover(request.Uid, pbCSMultgameGameoverReq.Parser.ParseFrom(request.PkgData)))];
}
