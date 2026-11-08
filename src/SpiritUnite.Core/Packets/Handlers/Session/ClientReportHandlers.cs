using Microsoft.Extensions.Logging;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Session;

public sealed class ClientReportHandlers(ILogger<ClientReportHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClientCdnreq)]
    public GamePacket[] OnClientCDNReq(GamePacket request)
    {
        var report = pbCSClientCDNReq.Parser.ParseFrom(request.PkgData);
        var host = Uri.TryCreate(report.Url, UriKind.Absolute, out var url) ? url.Host : string.Empty;
        logger.LogInformation("Client CDN download failed for uid {Uid}: host {Host}, error {ErrCode}, {DownloadedLength} bytes downloaded",
            request.Uid, host, report.ErrCode, report.DownloadedLength);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClientReportTlogReq)]
    public GamePacket[] OnClientReportTlogReq(GamePacket request)
    {
        pbCSClientReportTlogReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClientReportTlogRsp, new pbSCClientReportTlogRsp { ResultInfo = new pbResultInfo() })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidLobbyLatencyReportReq)]
    public GamePacket[] OnLobbyLatencyReportReq(GamePacket request)
    {
        pbCSLobbyLatencyReportReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidLobbyLatencyReportRsp, new pbSCLobbyLatencyReportRsp { ResultInfo = new pbResultInfo() })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGameGuideTlogReq)]
    public GamePacket[] OnGameGuideTlogReq(GamePacket request)
    {
        pbCSGameGuideTlogReq.Parser.ParseFrom(request.PkgData);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidReconnectInfoReport)]
    public GamePacket[] OnReconnectInfoReport(GamePacket request)
    {
        pbCSReconnectInfoReport.Parser.ParseFrom(request.PkgData);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidReconnectionTryTimesReport)]
    public GamePacket[] OnReconnectionTryTimesReport(GamePacket request)
    {
        pbCSReconnectionTryTimesReport.Parser.ParseFrom(request.PkgData);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClientDiskSpaceReq)]
    public GamePacket[] OnClientDiskSpaceReq(GamePacket request)
    {
        pbCSClientDiskSpaceReq.Parser.ParseFrom(request.PkgData);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidResDownProcessReportReq)]
    public GamePacket[] OnResDownProcessReportReq(GamePacket request)
    {
        pbCSResDownProcessReportReq.Parser.ParseFrom(request.PkgData);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidReportUseVoiceTlogReq)]
    public GamePacket[] OnReportUseVoiceTlogReq(GamePacket request)
    {
        var report = pbCSReportUseVoiceTlogReq.Parser.ParseFrom(request.PkgData);
        logger.LogDebug("Voice use report from uid {Uid}: scene {Sence}", request.Uid, report.Sence);
        return [];
    }
}
