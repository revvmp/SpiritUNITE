using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Lobby;

public sealed class LobbyHandlers
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetActionBindBusinessInfoReq)]
    public GamePacket[] OnGetActionBindBusinessInfoReq(GamePacket request)
    {
        pbCSGetActionBindBusinessInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetActionBindBusinessInfoRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetActionBindBusinessInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetActionBindBusinessShowFlagReq)]
    public GamePacket[] OnSetActionBindBusinessShowFlagReq(GamePacket request)
    {
        var showId = pbCSSetActionBindBusinessShowFlagReq.Parser.ParseFrom(request.PkgData).ShowId;
        var response = new pbSCSetActionBindBusinessShowFlagRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid },
            ShowId = showId,
            ShowInfo = new pbCOMDT_ACTION_BIND_BUSINESS_SHOW_INFO { ShowId = showId },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetActionBindBusinessShowFlagRsp, response)];
    }
}
