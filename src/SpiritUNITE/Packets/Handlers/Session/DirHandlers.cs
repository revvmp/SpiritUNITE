using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Servers.Lobby;

namespace SpiritUNITE.Packets.Handlers.Session;

public sealed class DirHandlers(IOptions<LobbyAddressOptions> lobbyAddress, ILogger<DirHandlers> logger)
{
    private const int ServerId = 1;

    [PacketHandler(eNetEventServerType.DirServer, PB_ENUM_CS_MSGID_DEF.PbCsidGameSvrAddrReq)]
    public GamePacket[] OnGameSvrAddrReq(GamePacket request)
    {
        logger.LogInformation("Sending Lobby address {Host}:{Port} to uid {Uid}",
            lobbyAddress.Value.Host, lobbyAddress.Value.Port, request.Uid);

        var response = new pbSCGameSvrAddrRsp
        {
            ResultInfo = new pbResultInfo(),
            GameSvrAddr = new pbCOMDT_GAME_SVR_ADDR
            {
                GameSvrUrl = lobbyAddress.Value.Host,
                GameSvrVport = lobbyAddress.Value.Port,
                ServerID = ServerId,
            },
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGameSvrAddrRsp, response)];
    }
}
