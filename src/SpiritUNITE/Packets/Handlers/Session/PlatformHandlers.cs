using SpiritUNITE.Auth;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Session;

public sealed class PlatformHandlers(AccountManager accounts)
{
    private const int PTCSLinked = 0;
    private const int NoAntiAddiction = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetBindChannelReq)]
    public async Task<GamePacket[]> OnGetBindChannelReq(GamePacket request)
    {
        pbCSGetBindChannelReq.Parser.ParseFrom(request.PkgData);
        var bound = await accounts.FindBoundChannelsAsync(request.Uid);
        var response = new pbSCGetBindChannelRsp
        {
            ResultInfo = new pbResultInfo(),
            ChannelList = { bound.Select(channel => (int)channel) },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetBindChannelRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetPtcslinkStatusReq)]
    public GamePacket[] OnGetPTCSLinkStatusReq(GamePacket request)
    {
        pbCSGetPTCSLinkStatusReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetPTCSLinkStatusRsp { ResultInfo = new pbResultInfo(), LinkStatus = PTCSLinked };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetPtcslinkStatusRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAgeVerifyReq)]
    public GamePacket[] OnGetAgeVerifyReq(GamePacket request)
    {
        pbCSGetAgeVerifyReq.Parser.ParseFrom(request.PkgData);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAntiAdditionInfoReq)]
    public GamePacket[] OnGetAntiAdditionInfoReq(GamePacket request)
    {
        pbCSGetAntiAdditionInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetAntiAdditionInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            AntiAdditionRspInfo = new pbCOMDT_ANTIADDITION_INFO { AntiadditionType = NoAntiAddiction },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAntiAdditionInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetClientSaveDataReq)]
    public GamePacket[] OnGetClientSaveDataReq(GamePacket request)
    {
        pbCSGetClientSaveDataReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetClientSaveDataRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetClientSaveDataRsp, response)];
    }
}
