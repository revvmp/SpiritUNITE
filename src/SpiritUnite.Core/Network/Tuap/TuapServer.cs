using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Network.Gcp;
using SpiritUnite.Tools;

namespace SpiritUnite.Network.Tuap;

public abstract class TuapServer(
    string serverName,
    int port,
    IOptions<TcpServerOptions> options,
    TimeProvider time,
    BackgroundOperations operations,
    ILogger<TuapServer> logger) : BackgroundService
{
    private const int InitialReceiveBufferSize = 4096;

    private readonly TcpListener listener = new(IPAddress.Parse(options.Value.BindAddress), port);

    private int activeConnections;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        listener.Start();
        logger.LogInformation("{ServerName} listening on {EndPoint}", serverName, listener.LocalEndpoint);
        return base.StartAsync(cancellationToken);
    }

    public override void Dispose()
    {
        listener.Dispose();
        base.Dispose();
    }

    protected abstract TuapService? FindService(string name);

    protected abstract Task<byte[]?> HandleRequestAsync(TuapService service, ReadOnlyMemory<byte> payload, EndPoint? remoteEndPoint);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken);
                Accept(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("{ServerName} stopped listening", serverName);
        }
    }

    private void Accept(TcpClient client, CancellationToken stoppingToken)
    {
        if (Interlocked.Increment(ref activeConnections) > options.Value.MaxConnections)
        {
            logger.LogWarning("{ServerName} refused {RemoteEndPoint}: limit of {MaxConnections} connections reached",
                serverName, client.Client.RemoteEndPoint, options.Value.MaxConnections);
            client.Dispose();
            Interlocked.Decrement(ref activeConnections);
            return;
        }

        if (!operations.Run("TUAP connection", async token =>
        {
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(token, stoppingToken);
            await ServeAsync(client, stopped.Token);
        }))
        {
            client.Dispose();
            Interlocked.Decrement(ref activeConnections);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stoppingToken)
    {
        try
        {
            using (client)
            {
                await RunAsync(client, stoppingToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{ServerName} connection failed unexpectedly", serverName);
        }
        finally
        {
            Interlocked.Decrement(ref activeConnections);
        }
    }

    private async Task RunAsync(TcpClient client, CancellationToken stoppingToken)
    {
        var stream = client.GetStream();
        var remoteEndPoint = client.Client.RemoteEndPoint;
        using var session = new TuapSession(
            FindService,
            (service, payload) => HandleRequestAsync(service, payload, remoteEndPoint),
            packet => stream.WriteAsync(packet, stoppingToken).AsTask(),
            time);

        try
        {
            await ReceiveUntilClosedAsync(stream, session, stoppingToken);
            logger.LogDebug("{ServerName} closed {RemoteEndPoint} {Service}", serverName, remoteEndPoint, session.ServiceName);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{ServerName} closed {RemoteEndPoint} for shutdown", serverName, remoteEndPoint);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("{ServerName} closed idle connection {RemoteEndPoint}", serverName, remoteEndPoint);
        }
        catch (IOException exception)
        {
            logger.LogInformation("{ServerName} lost {RemoteEndPoint}: {Reason}", serverName, remoteEndPoint, exception.Message);
        }
        catch (GcpProtocolException exception)
        {
            logger.LogWarning("{ServerName} refused {RemoteEndPoint}: {Reason}", serverName, remoteEndPoint, exception.Message);
        }
    }

    private async Task ReceiveUntilClosedAsync(NetworkStream stream, TuapSession session, CancellationToken stoppingToken)
    {
        var buffer = GC.AllocateArray<byte>(InitialReceiveBufferSize, pinned: true);
        var buffered = 0;
        var idleTimeout = TimeSpan.FromSeconds(options.Value.IdleTimeoutSeconds);

        while (true)
        {
            if (buffered == buffer.Length)
            {
                if (buffer.Length == TuapPacket.MaxPacketLength)
                {
                    throw new GcpProtocolException($"TUAP packet exceeds {TuapPacket.MaxPacketLength} bytes");
                }

                var grown = GC.AllocateArray<byte>(Math.Min(buffer.Length * 2, TuapPacket.MaxPacketLength), pinned: true);
                buffer.AsSpan(0, buffered).CopyTo(grown);
                buffer = grown;
            }

            using var idleTimer = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            idleTimer.CancelAfter(idleTimeout);

            var received = await stream.ReadAsync(buffer.AsMemory(buffered), idleTimer.Token);
            if (received == 0)
            {
                return;
            }

            buffered += received;
            var consumed = 0;
            while (TuapPacket.TryParseClient(buffer.AsMemory(consumed, buffered - consumed), out var head)
                && head.PacketLength <= buffered - consumed)
            {
                var packet = buffer.AsMemory(consumed, head.PacketLength);
                var keepOpen = await session.HandleAsync(head, packet[head.HeadLength..]);
                consumed += head.PacketLength;
                if (!keepOpen)
                {
                    return;
                }
            }

            buffer.AsSpan(consumed, buffered - consumed).CopyTo(buffer);
            buffered -= consumed;
        }
    }
}
