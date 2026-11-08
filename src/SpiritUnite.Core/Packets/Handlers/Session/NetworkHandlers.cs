using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Session;

public sealed class NetworkHandlers
{
    private const int NearestAccessOff = 0;
    private const int PingIntervalSeconds = 3600;
    private const int PingPacketsPerRound = 1;
    private const int PingTimeoutSeconds = 1;
    private const int NoLoginPings = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidNetworkPingIdcInfoReq)]
    public GamePacket[] OnNetworkPingIdcInfoReq(GamePacket request)
    {
        pbCSNetworkPingIdcInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCNetworkPingIdcInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            NetworkPingIdcInfo = new pbCOMDT_NETWORK_PING_IDC_INFO(),
            PingInterval = PingIntervalSeconds,
            PingPkgNum = PingPacketsPerRound,
            PingTimeout = PingTimeoutSeconds,
            LoginPingInterval = PingIntervalSeconds,
            LoginPingCount = NoLoginPings,
            NearestAccessSwitch = NearestAccessOff,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNetworkPingIdcInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidNetworkPingReportReq)]
    public GamePacket[] OnNetworkPingReportReq(GamePacket request)
    {
        var report = pbCSNetworkPingReportReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCNetworkPingReportRsp
        {
            ResultInfo = new pbResultInfo(),
            NetworkInfo = new pbCOMDT_NETWORK_INFO
            {
                NetworkRecordList =
                {
                    report.NetworkPingInfo?.NetworkPingRecordList.Select(record => new pbCOMDT_NETWORK_INFO_RECORD { IdcTag = record.IdcTag }) ?? [],
                },
            },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidNetworkPingReportRsp, response)];
    }
}
