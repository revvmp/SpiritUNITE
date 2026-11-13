using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SpiritUnite.Network.Gcp;
using SpiritUnite.Packets;

namespace SpiritUnite.Network;

public sealed class Connection(
    TcpClient client,
    TimeSpan idleTimeout,
    TimeSpan registrationIdleTimeout,
    eNetEventServerType serverType,
    Func<ulong, GamePacket[]> greeting,
    PacketDispatcher dispatcher,
    Func<GcpAuthReq, Task<ulong>> authenticate,
    GcpSessionHooks hooks,
    GcpCompression compression,
    ILogger<Connection> logger)
{
    private const int InitialReceiveBufferSize = 4096;
    private const int MaxLoggedBytes = 64;

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        using (client)
        {
            var stream = client.GetStream();
            var socket = client.Client;
            await using var session = new GcpSession(
                serverType,
                greeting,
                dispatcher,
                authenticate,
                packet => stream.WriteAsync(packet, stoppingToken).AsTask(),
                Shutdown,
                hooks,
                compression,
                logger)
            {
                MeasureRtt = () => TcpRtt.Measure(socket),
                RemoteAddress = socket.RemoteEndPoint is IPEndPoint remote
                    ? remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address
                    : null,
            };
            var remoteEndPoint = socket.RemoteEndPoint;
            logger.LogInformation("Connected {RemoteEndPoint}", remoteEndPoint);

            try
            {
                await ReceiveUntilClosedAsync(stream, session, stoppingToken);
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
            catch (ObjectDisposedException) when (!session.IsOpen)
            {
                logger.LogInformation("Closed superseded connection {RemoteEndPoint}", remoteEndPoint);
            }
            catch (GcpProtocolException exception)
            {
                logger.LogWarning("Refused {RemoteEndPoint}: {Reason}", remoteEndPoint, exception.Message);
            }
        }
    }

    private void Shutdown()
    {
        try
        {
            client.Client.Shutdown(SocketShutdown.Both);
        }
        catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
        {
            logger.LogDebug("Connection was already closed: {Reason}", exception.Message);
        }
    }

    private async Task ReceiveUntilClosedAsync(NetworkStream stream, GcpSession session, CancellationToken stoppingToken)
    {
        var buffer = GC.AllocateArray<byte>(InitialReceiveBufferSize, pinned: true);
        var buffered = 0;

        while (true)
        {
            if (buffered == buffer.Length)
            {
                if (buffer.Length == GcpHead.MaxPacketSize)
                {
                    throw new GcpProtocolException("Receive buffer exceeded the maximum packet size");
                }

                var grown = GC.AllocateArray<byte>(Math.Min(buffer.Length * 2, GcpHead.MaxPacketSize), pinned: true);
                buffer.AsSpan(0, buffered).CopyTo(grown);
                buffer = grown;
            }

            using var idleTimer = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            idleTimer.CancelAfter(session.AwaitingRegistration ? registrationIdleTimeout : idleTimeout);

            var received = await stream.ReadAsync(buffer.AsMemory(buffered), idleTimer.Token);
            if (received == 0)
            {
                return;
            }

            LogReceived(buffer.AsSpan(buffered, received));
            buffered += received;

            var consumed = await HandleCompletePacketsAsync(session, buffer.AsMemory(0, buffered));
            buffer.AsSpan(consumed, buffered - consumed).CopyTo(buffer);
            buffered -= consumed;
        }
    }

    private static async Task<int> HandleCompletePacketsAsync(GcpSession session, ReadOnlyMemory<byte> data)
    {
        var consumed = 0;

        while (GcpHead.TryParse(data.Span[consumed..], out var head) && head.PacketLength <= data.Length - consumed)
        {
            await session.HandleAsync(head, data.Slice(consumed, head.PacketLength));

            consumed += head.PacketLength;
        }

        return consumed;
    }

    private void LogReceived(ReadOnlySpan<byte> received)
    {
        if (!logger.IsEnabled(LogLevel.Trace))
        {
            return;
        }

        var loggedBytes = received[..Math.Min(received.Length, MaxLoggedBytes)];
        logger.LogTrace("RECV {Size} bytes: {Hex}", received.Length, Convert.ToHexString(loggedBytes));
    }
}
