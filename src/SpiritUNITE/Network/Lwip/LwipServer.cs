using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Network.Gcp;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Network.Lwip;

public abstract class LwipServer(
    eNetEventServerType serverType,
    string advertisedHost,
    int port,
    GcpCompression compression,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    LwipTickets tickets,
    ILogger<LwipServer> logger) : BackgroundService
{
    protected const ulong NoAccount = 0;

    private const int MaxDatagramSize = 2048;
    private const int ReceiveBufferSize = ushort.MaxValue;
    private const int SioUdpConnectionReset = unchecked((int)0x9800000C);
    private const long ReconnectQuietMs = 5000;
    private const int IdleCloseExCode = 0x10000001;
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(10);
    private static readonly byte[] ConnectionResetReportsOff = [0, 0, 0, 0];

    private readonly IPAddress bindAddress = IPAddress.Parse(options.Value.BindAddress);
    private readonly ConcurrentDictionary<LwipFlowKey, LwipFlow> flows = new();
    private Socket? socket;
    private uint[] relayMixes = [];

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var udp = new Socket(bindAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (bindAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                udp.DualMode = true;
            }

            if (OperatingSystem.IsWindows())
            {
                udp.IOControl(SioUdpConnectionReset, ConnectionResetReportsOff, null);
            }

            udp.Bind(new IPEndPoint(bindAddress, port));
        }
        catch
        {
            udp.Dispose();
            throw;
        }

        socket = udp;
        relayMixes = await ResolveRelayMixesAsync(cancellationToken);
        logger.LogInformation("{ServerType} listening on udp {EndPoint}", serverType, udp.LocalEndPoint);
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var flow in flows.Values)
        {
            flow.Close();
        }
    }

    public override void Dispose()
    {
        socket?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(ReceiveUntilStoppedAsync(socket!, stoppingToken), TickUntilStoppedAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Stopped listening");
        }
    }

    protected virtual GamePacket[] BuildGreeting(ulong authenticatedUid) => [];

    protected virtual Task<ulong> AuthenticateAsync(GcpAuthReq request) => Task.FromResult(NoAccount);

    protected virtual GcpSessionHooks SessionHooks => GcpSessionHooks.None;

    private async Task<uint[]> ResolveRelayMixesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var addresses = IPAddress.TryParse(advertisedHost, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(advertisedHost, cancellationToken);
            return [.. addresses.Select(address => LwipEnvelope.ZsdRelayMix(address, port)).Distinct()];
        }
        catch (SocketException exception)
        {
            logger.LogError("Relay host {Host} does not resolve, so no relay datagram can be verified: {Reason}", advertisedHost, exception.Message);
            return [];
        }
    }

    private async Task ReceiveUntilStoppedAsync(Socket udp, CancellationToken stoppingToken)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(ReceiveBufferSize);
        EndPoint anyEndPoint = new IPEndPoint(bindAddress.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, IPEndPoint.MinPort);

        while (true)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await udp.ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint, stoppingToken);
            }
            catch (SocketException exception)
            {
                logger.LogDebug("Relay receive failed: {Reason}", exception.Message);
                continue;
            }

            try
            {
                Receive(udp, buffer.AsMemory(0, received.ReceivedBytes), received.RemoteEndPoint, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogDebug(exception, "Dropped a relay datagram from {RemoteEndPoint}", received.RemoteEndPoint);
            }
        }
    }

    private async Task TickUntilStoppedAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = Environment.TickCount64;
            foreach (var flow in flows.Values)
            {
                if (!tickets.IsIssued(flow.Ticket))
                {
                    flow.Close();
                    continue;
                }

                try
                {
                    flow.Tick(now);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Relay flow tick failed");
                    flow.Close();
                }
            }
        }
    }

    private void Receive(Socket udp, ReadOnlyMemory<byte> datagram, EndPoint source, CancellationToken stoppingToken)
    {
        if (datagram.Length > MaxDatagramSize
            || !LwipEnvelope.TryReadZsdPrefix(datagram.Span, out var prefix)
            || tickets.Find(prefix.Id) is not { } ticket
            || !IsTagValid(ticket, prefix)
            || !LwipEnvelope.TryParseSegment(datagram[LwipEnvelope.ZsdPrefixSize..], out var segment)
            || segment.DestinationPort != port
            || !segment.Has(LwipFlags.Unreliable) && segment.Payload.Length > LwipEnvelope.MaxSegmentSize)
        {
            return;
        }

        var key = new LwipFlowKey(ticket.Id, segment.SourcePort);
        var now = Environment.TickCount64;
        if (flows.TryGetValue(key, out var flow) && !flow.IsReplacedBy(segment))
        {
            if (flow.Receive(segment, prefix.Counter, source))
            {
                ticket.RecordCounter(prefix.Counter, now);
            }

            return;
        }

        if (segment.Has(LwipFlags.Syn) && !segment.Has(LwipFlags.Ack))
        {
            Open(udp, key, ticket, segment, prefix.Counter, source, now, stoppingToken);
        }
        else if (!segment.Has(LwipFlags.Rst) && !segment.Has(LwipFlags.Unreliable))
        {
            SendReset(udp, segment, source);
        }
    }

    private bool IsTagValid(LwipTicket ticket, ZsdPrefix prefix)
    {
        foreach (var relayMix in relayMixes)
        {
            if (LwipEnvelope.ZsdTag(ticket.Chain, ticket.CodeMix ^ relayMix, prefix.Counter) == prefix.Tag)
            {
                return true;
            }
        }

        return false;
    }

    private void Open(Socket udp, LwipFlowKey key, LwipTicket ticket, LwipSegment syn, uint counter, EndPoint source, long now, CancellationToken stoppingToken)
    {
        if (flows.Count >= options.Value.MaxConnections)
        {
            logger.LogWarning("Refused relay flow from {RemoteEndPoint}: limit of {MaxConnections} flows reached", source, options.Value.MaxConnections);
            return;
        }

        var requiredQuietMs = flows.Values.Any(previous => previous.Ticket == ticket) ? ReconnectQuietMs : 0;
        if (!ticket.TryClaimNewFlow(counter, now, requiredQuietMs))
        {
            logger.LogInformation("Dropped a relay SYN from {RemoteEndPoint} for uid {Uid}: its ZSD counter is not above the live flow's and that flow is still active",
                source, ticket.Uid);
            return;
        }

        foreach (var previous in flows.Values.Where(previous => previous.Ticket == ticket))
        {
            previous.Close();
        }

        var flow = new LwipFlow(key, ticket, (ushort)port, syn, counter, source, udp, closed => flows.TryRemove(new KeyValuePair<LwipFlowKey, LwipFlow>(closed.Key, closed)));
        flows[key] = flow;
        logger.LogInformation("Relay flow from {RemoteEndPoint} opened for uid {Uid}", source, ticket.Uid);
        _ = ServeAsync(flow, stoppingToken);
    }

    private void SendReset(Socket udp, LwipSegment segment, EndPoint source)
    {
        var reset = new LwipSegment(
            (ushort)port,
            segment.SourcePort,
            segment.Ack,
            segment.Seq + (uint)segment.Payload.Length,
            LwipFlags.Rst | LwipFlags.Ack,
            LwipEnvelope.Window,
            TimestampEcho: 0,
            (ushort)Environment.TickCount64,
            ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty);
        try
        {
            udp.SendTo(LwipEnvelope.BuildSegment(reset), source);
        }
        catch (SocketException exception)
        {
            logger.LogDebug("Relay reset to {RemoteEndPoint} failed: {Reason}", source, exception.Message);
        }
    }

    private async Task ServeAsync(LwipFlow flow, CancellationToken stoppingToken)
    {
        try
        {
            await RunSessionAsync(flow, stoppingToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Relay flow failed unexpectedly");
        }
        finally
        {
            flow.Close();
        }
    }

    private async Task RunSessionAsync(LwipFlow flow, CancellationToken stoppingToken)
    {
        await using var session = new GcpSession(
            serverType,
            BuildGreeting,
            dispatcher,
            request => AuthenticateTicketAsync(flow, request),
            flow.WriteStreamAsync,
            flow.Close,
            SessionHooks,
            compression,
            logger)
        {
            WriteUnreliable = flow.WriteUnreliableAsync,
            Style = GcpSessionStyle.Relay,
        };
        var stream = new GcpStreamBuffer();

        try
        {
            await foreach (var item in flow.Inbound.ReadAllAsync(stoppingToken))
            {
                if (flow.IsClosed)
                {
                    break;
                }

                await HandleInboundAsync(flow, session, stream, item);
            }

            logger.LogInformation("Relay flow from {RemoteEndPoint} for uid {Uid} closed", flow.Remote, flow.Ticket.Uid);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Closed relay flow {RemoteEndPoint} for shutdown", flow.Remote);
        }
        catch (IOException exception)
        {
            logger.LogInformation("Relay flow {RemoteEndPoint} lost: {Reason}", flow.Remote, exception.Message);
        }
        catch (GcpProtocolException exception)
        {
            logger.LogWarning("Refused relay flow {RemoteEndPoint} for uid {Uid}: {Reason}", flow.Remote, flow.Ticket.Uid, exception.Message);
        }
    }

    private async Task HandleInboundAsync(LwipFlow flow, GcpSession session, GcpStreamBuffer stream, LwipInbound item)
    {
        switch (item.Kind)
        {
            case LwipInboundKind.Stream:
                foreach (var (head, packet) in stream.Append(item.Data))
                {
                    await session.HandleAsync(head, packet);
                    if (head.Command == (ushort)GcpCommand.Syn)
                    {
                        LogSynToken(flow, session.SynReserved);
                    }
                }

                break;
            case LwipInboundKind.UnreliableBody:
                await HandleUnreliableAsync(flow, () => session.HandleUnreliableBodyAsync(item.Data, item.Compressed));
                break;
            case LwipInboundKind.UnreliablePacket:
                await HandleUnreliableAsync(flow, () => HandleUnreliablePacketAsync(session, item.Data));
                break;
            case LwipInboundKind.IdleClose:
                logger.LogInformation("Relay flow {RemoteEndPoint} for uid {Uid} sent no stream segment for its keepalive window", flow.Remote, flow.Ticket.Uid);
                await session.StopAsync(GcpStopReason.IdleClose, IdleCloseExCode);
                break;
        }
    }

    private static Task HandleUnreliablePacketAsync(GcpSession session, byte[] packet) =>
        GcpHead.TryParse(packet, out var head) && head.PacketLength == packet.Length && head.Command == (ushort)GcpCommand.Data
            ? session.HandleAsync(head, packet)
            : throw new GcpProtocolException("Unreliable datagram is not one complete data packet");

    private async Task HandleUnreliableAsync(LwipFlow flow, Func<Task> handle)
    {
        try
        {
            await handle();
        }
        catch (GcpProtocolException exception)
        {
            logger.LogDebug("Dropped an unreliable datagram from {RemoteEndPoint}: {Reason}", flow.Remote, exception.Message);
        }
    }

    private void LogSynToken(LwipFlow flow, uint? synReserved)
    {
        if (synReserved != unchecked((uint)flow.Ticket.Token))
        {
            logger.LogInformation("Relay SYN from {RemoteEndPoint} for uid {Uid} does not carry the ConnRelayNtf token, as a client reconnect does",
                flow.Remote, flow.Ticket.Uid);
        }
    }

    private async Task<ulong> AuthenticateTicketAsync(LwipFlow flow, GcpAuthReq request)
    {
        if (!tickets.IsIssued(flow.Ticket))
        {
            throw new GcpProtocolException("Relay ticket was revoked");
        }

        var uid = await AuthenticateAsync(request);
        if (uid == NoAccount || uid != flow.Ticket.Uid)
        {
            throw new GcpProtocolException("Auth token does not belong to the ticket's uid");
        }

        return uid;
    }

    private sealed class GcpStreamBuffer
    {
        private readonly byte[] buffer = new byte[GcpHead.MaxPacketSize + LwipEnvelope.MaxSegmentSize];
        private int buffered;

        public IEnumerable<(GcpHead Head, ReadOnlyMemory<byte> Packet)> Append(byte[] data)
        {
            if (data.Length > buffer.Length - buffered)
            {
                throw new GcpProtocolException("Relay stream overflowed the packet buffer");
            }

            data.CopyTo(buffer.AsSpan(buffered));
            buffered += data.Length;
            return TakeCompletePackets();
        }

        private List<(GcpHead Head, ReadOnlyMemory<byte> Packet)> TakeCompletePackets()
        {
            var packets = new List<(GcpHead Head, ReadOnlyMemory<byte> Packet)>();
            var consumed = 0;
            while (GcpHead.TryParse(buffer.AsSpan(consumed, buffered - consumed), out var head) && head.PacketLength <= buffered - consumed)
            {
                packets.Add((head, buffer.AsSpan(consumed, head.PacketLength).ToArray()));
                consumed += head.PacketLength;
            }

            buffer.AsSpan(consumed, buffered - consumed).CopyTo(buffer);
            buffered -= consumed;
            return packets;
        }
    }
}
