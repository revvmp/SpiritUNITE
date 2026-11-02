using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Network.Gcp;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Network;

public sealed class Connection(
    TcpClient client,
    TimeSpan idleTimeout,
    PacketDispatcher dispatcher,
    ILogger<Connection> logger)
{
    private const int MaxLoggedBytes = 64;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using (client)
        using (var session = new GcpSession(dispatcher, logger))
        {
            var remoteEndPoint = client.Client.RemoteEndPoint;
            logger.LogInformation("Connected {RemoteEndPoint}", remoteEndPoint);

            try
            {
                await ReceiveUntilClosedAsync(client.GetStream(), session, stoppingToken);
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
            catch (GcpProtocolException exception)
            {
                logger.LogWarning("Refused {RemoteEndPoint}: {Reason}", remoteEndPoint, exception.Message);
            }
        }
    }

    private async Task ReceiveUntilClosedAsync(
        NetworkStream stream,
        GcpSession session,
        CancellationToken stoppingToken)
    {
        var buffer = new byte[GcpHead.MaxPacketSize];
        var buffered = 0;

        while (true)
        {
            using var idleTimer = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            idleTimer.CancelAfter(idleTimeout);

            var received = await stream.ReadAsync(buffer.AsMemory(buffered), idleTimer.Token);
            if (received == 0)
            {
                return;
            }

            LogReceived(buffer.AsSpan(buffered, received));
            buffered += received;

            var consumed = await HandleCompletePacketsAsync(stream, session, buffer.AsMemory(0, buffered), stoppingToken);
            buffer.AsSpan(consumed, buffered - consumed).CopyTo(buffer);
            buffered -= consumed;
        }
    }

    private static async Task<int> HandleCompletePacketsAsync(
        NetworkStream stream,
        GcpSession session,
        ReadOnlyMemory<byte> data,
        CancellationToken stoppingToken)
    {
        var consumed = 0;

        while (GcpHead.TryParse(data.Span[consumed..], out var head) && head.PacketLength <= data.Length - consumed)
        {
            var packet = data.Slice(consumed, head.PacketLength);
            foreach (var reply in session.Handle(head, packet))
            {
                await stream.WriteAsync(reply, stoppingToken);
            }

            consumed += head.PacketLength;
        }

        return consumed;
    }

    private void LogReceived(ReadOnlySpan<byte> received)
    {
        var loggedBytes = received[..Math.Min(received.Length, MaxLoggedBytes)];
        logger.LogTrace("RECV {Size} bytes: {Hex}", received.Length, Convert.ToHexString(loggedBytes));
    }
}
