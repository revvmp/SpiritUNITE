using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Network;
using SpiritUnite.Packets;
using SpiritUnite.Tools;

namespace SpiritUnite.Servers.Dir;

public sealed class DirServer(
    IOptions<DirServerOptions> dirOptions,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    BackgroundOperations operations,
    ILogger<TcpServer> logger,
    ILogger<Connection> connectionLogger)
    : TcpServer(eNetEventServerType.DirServer, dirOptions.Value.Port, options, dispatcher, operations, logger, connectionLogger);
