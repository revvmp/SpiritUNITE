using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Network;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Servers.Dir;

public sealed class DirServer(
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    ILogger<TcpServer> logger,
    ILogger<Connection> connectionLogger)
    : TcpServer(Connector.Dir, options.Value.Port, options, dispatcher, logger, connectionLogger);
