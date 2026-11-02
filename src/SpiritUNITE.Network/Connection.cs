using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SpiritUNITE.Network;

public sealed class Connection(TcpClient client, TimeSpan idleTimeout, ILogger<Connection> logger)
{
    private const int ReceiveBufferSize = 4096;
    private const int MaxLoggedBytes = 64;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using (client)
        {
            var remoteEndPoint = client.Client.RemoteEndPoint;
            logger.LogInformation("Connected {RemoteEndPoint}", remoteEndPoint);

            try
            {
                await ReceiveUntilClosedAsync(client.GetStream(), stoppingToken);
                logger.LogInformation("Disconnected {RemoteEndPoint}", remoteEndPoint);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("Closed {RemoteEndPoint} for shutdown", remoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Closed idle connection {RemoteEndPoint}", remoteEndPoint);
            }
            catch (IOException exception)
            {
                logger.LogInformation("Connection lost {RemoteEndPoint}: {Reason}", remoteEndPoint, exception.Message);
            }
        }
    }

    private async Task ReceiveUntilClosedAsync(NetworkStream stream, CancellationToken stoppingToken)
    {
        var buffer = new byte[ReceiveBufferSize];

        while (true)
        {
            using var idleTimer = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            idleTimer.CancelAfter(idleTimeout);

            var received = await stream.ReadAsync(buffer, idleTimer.Token);
            if (received == 0)
            {
                return;
            }

            LogReceived(buffer.AsSpan(0, received));
        }
    }

    private void LogReceived(ReadOnlySpan<byte> received)
    {
        var loggedBytes = received[..Math.Min(received.Length, MaxLoggedBytes)];
        logger.LogDebug("RECV {Size} bytes: {Hex}", received.Length, Convert.ToHexString(loggedBytes));
    }
}
