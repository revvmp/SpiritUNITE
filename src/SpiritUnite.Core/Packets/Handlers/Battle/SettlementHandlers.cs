using Microsoft.Extensions.Logging;
using SpiritUnite.Battle;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Battle;

public sealed class SettlementHandlers(BattleSettlement settlement, SettleFabulous fabulous, ILogger<SettlementHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSettleFabulousReq)]
    public async Task<GamePacket[]> OnSettleFabulousReq(GamePacket request)
    {
        var beFabulousUid = pbCSSettleFabulousReq.Parser.ParseFrom(request.PkgData).BeFabulousUid;
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSettleFabulousRsp, await fabulous.FabulousAsync(request.Uid, beFabulousUid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMultiGameSettleGainReq)]
    public async Task<GamePacket[]> OnMultiGameSettleGainReq(GamePacket request)
    {
        pbCSMultiGameSettleGainReq.Parser.ParseFrom(request.PkgData);
        if (await settlement.FindSettleGainAsync(request.Uid) is not { } gain)
        {
            logger.LogInformation("Uid {Uid} asked for a settlement it does not have", request.Uid);
            return [];
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMultiGameSettleGainRsp, gain)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSingleGameOverReq)]
    public GamePacket[] OnSingleGameOverReq(GamePacket request)
    {
        var gameOver = pbCSSingleGameOverReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation(
            "Uid {Uid} finished a local single game: GradingPointValid {GradingPointValid}, GradingPoint {GradingPoint}",
            request.Uid,
            gameOver.GradingPointValid,
            gameOver.GradingPoint);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSingleGameOverRsp, new pbSCSingleGameOverRsp { ResultInfo = new pbResultInfo() })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGameOverReportNewbieBattleTlogReq)]
    public GamePacket[] OnGameOverReportNewbieBattleTlogReq(GamePacket request)
    {
        pbCSGameOverReportNewbieBattleTlogReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidGameOverReportNewbieBattleTlogRsp,
                new pbSCGameOverReportNewbieBattleTlogRsp { ResultInfo = new pbResultInfo() }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGameOverReportAcntTlogReq)]
    public GamePacket[] OnGameOverReportAcntTlogReq(GamePacket request)
    {
        pbCSGameOverReportAcntTlogReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbCsidGameOverReportAcntTlogRsp,
                new pbCSGameOverReportAcntTlogRsp { ResultInfo = new pbResultInfo() }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGameOverReportDeskTlogReq)]
    public GamePacket[] OnGameOverReportDeskTlogReq(GamePacket request)
    {
        pbCSGameOverReportDeskTlogReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbCsidGameOverReportDeskTlogRsp,
                new pbCSGameOverReportDeskTlogRsp { ResultInfo = new pbResultInfo() }),
        ];
    }
}
