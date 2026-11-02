using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Network;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Servers.Lobby;

public sealed class LobbyServer(
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    ILogger<TcpServer> logger,
    ILogger<Connection> connectionLogger)
    : TcpServer(Connector.Lobby, lobbyAddress.Value.Port, options, dispatcher, logger, connectionLogger)
{
    protected override GamePacket[] BuildGreeting() =>
        [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidLoginNowNtf, new pbSCLoginNowNtf())];
}
