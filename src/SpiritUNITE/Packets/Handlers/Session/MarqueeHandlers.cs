using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Session;

public sealed class MarqueeHandlers
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetMarqueeListReq)]
    public GamePacket[] OnGetMarqueeListReq(GamePacket request)
    {
        pbCSGetMarqueeListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetMarqueeListRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetMarqueeListRsp, response)];
    }
}
