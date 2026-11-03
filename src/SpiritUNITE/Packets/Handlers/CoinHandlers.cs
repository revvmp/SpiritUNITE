using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class CoinHandlers
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetCoinValReq)]
    public GamePacket[] OnGetCoinValReq(GamePacket request)
    {
        pbCSGetCoinValReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetCoinValNtf { CoinInfo = new pbCOMDT_COIN_INFO() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, response)];
    }
}
