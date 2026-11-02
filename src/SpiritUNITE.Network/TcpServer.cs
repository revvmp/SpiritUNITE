using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpiritUNITE.Network;

public sealed class TcpServer(
    IOptions<TcpServerOptions> options,
    ILogger<TcpServer> logger,
    ILogger<Connection> connectionLogger) : BackgroundService
{
    private readonly TcpListener listener = new(
        IPAddress.Parse(options.Value.BindAddress),
        options.Value.Port);

    private int activeConnections;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        listener.Start();
        logger.LogInformation("Listening on {EndPoint}", listener.LocalEndpoint);
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
        _ = ServeAsync(new Connection(client, idleTimeout, connectionLogger), stoppingToken);
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
