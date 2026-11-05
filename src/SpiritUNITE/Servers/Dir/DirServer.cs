using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Network;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Servers.Dir;

public sealed class DirServer(
    IOptions<DirServerOptions> dirOptions,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    ILogger<TcpServer> logger,
    ILogger<Connection> connectionLogger)
    : TcpServer(eNetEventServerType.DirServer, dirOptions.Value.Port, options, dispatcher, logger, connectionLogger);
