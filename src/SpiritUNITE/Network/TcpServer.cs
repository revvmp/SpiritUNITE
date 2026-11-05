using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Network.Gcp;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Network;

public abstract class TcpServer(
    eNetEventServerType serverType,
    int port,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    ILogger<TcpServer> logger,
    ILogger<Connection> connectionLogger) : BackgroundService
{
    protected const ulong NoAccount = 0;

    private readonly TcpListener listener = new(
        IPAddress.Parse(options.Value.BindAddress),
        port);

    private int activeConnections;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        listener.Start();
        logger.LogInformation("{ServerType} listening on {EndPoint}", serverType, listener.LocalEndpoint);
        return base.StartAsync(cancellationToken);
    }

    public override void Dispose()
    {
        listener.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                Accept(client, options.Value, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Stopped listening");
        }
    }

    protected virtual GamePacket[] BuildGreeting(ulong authenticatedUid) => [];

    protected virtual Task<ulong> AuthenticateAsync(GcpAuthReq request) => Task.FromResult(NoAccount);

    protected virtual GcpSessionHooks SessionHooks => GcpSessionHooks.None;

    protected virtual GcpCompression Compression => GcpCompression.Lz4;

    private void Accept(TcpClient client, TcpServerOptions settings, CancellationToken stoppingToken)
    {
        if (Interlocked.Increment(ref activeConnections) > settings.MaxConnections)
        {
            logger.LogWarning("Refused {RemoteEndPoint}: limit of {MaxConnections} connections reached",
                client.Client.RemoteEndPoint, settings.MaxConnections);
            client.Dispose();
            Interlocked.Decrement(ref activeConnections);
            return;
        }

        var idleTimeout = TimeSpan.FromSeconds(settings.IdleTimeoutSeconds);
        var registrationIdleTimeout = TimeSpan.FromSeconds(settings.RegistrationIdleTimeoutSeconds);
        _ = ServeAsync(new Connection(client, idleTimeout, registrationIdleTimeout, serverType, BuildGreeting, dispatcher, AuthenticateAsync, SessionHooks, Compression, connectionLogger), stoppingToken);
    }

    private async Task ServeAsync(Connection connection, CancellationToken stoppingToken)
    {
        try
        {
            await connection.RunAsync(stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Connection failed unexpectedly");
        }
        finally
        {
            Interlocked.Decrement(ref activeConnections);
        }
    }
}
