using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Game.BattleVoice;
using SpiritUnite.Network;
using SpiritUnite.Network.GVoice;
using SpiritUnite.Servers.Lobby;
using SpiritUnite.Tools;

namespace SpiritUnite.Servers.Voice;

public sealed class VoiceServer(
    GVoiceRoomManager voiceRooms,
    IOptions<VoiceServerOptions> options,
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<TcpServerOptions> tcpOptions,
    TimeProvider clock,
    ILogger<VoiceServer> logger) : BackgroundService
{
    private const int ClientReceiveBufferSize = 0x2800;
    private const int ReceiveBufferSize = ushort.MaxValue;
    private const int SioUdpConnectionReset = unchecked((int)0x9800000C);
    private const int GetConfigRepliesPerSource = 30;
    private const long GetConfigReplyRefillMs = 2_000;
    private const int GetConfigRepliesBurst = 1_000;
    private const long GetConfigReplyGlobalRefillMs = 1;
    private const int JoinsPerSource = 20;
    private const long JoinRefillMs = 1_000;
    private const int JoinRefusalsPerSource = 5;
    private const long JoinRefusalRefillMs = 12_000;
    private const int MaxTrackedSources = 16_384;
    private const int AnySource = 0;
    private const long QuietSourceSweepMs = 10_000;
    private static readonly TimeSpan ExpiryInterval = TimeSpan.FromSeconds(1);
    private static readonly byte[] ConnectionResetReportsOff = [0, 0, 0, 0];

    private readonly KeyedTokenBuckets<IPAddress> getConfigReplies = new(GetConfigRepliesPerSource, GetConfigReplyRefillMs, MaxTrackedSources);
    private readonly KeyedTokenBuckets<int> allGetConfigReplies = new(GetConfigRepliesBurst, GetConfigReplyGlobalRefillMs, 1);
    private readonly KeyedTokenBuckets<IPAddress> joins = new(JoinsPerSource, JoinRefillMs, MaxTrackedSources);
    private readonly KeyedTokenBuckets<IPAddress> joinRefusals = new(JoinRefusalsPerSource, JoinRefusalRefillMs, MaxTrackedSources);
    private long lastQuietSourceSweepMs;
    private long unansweredAuthKeyRequests;

    private readonly IPAddress bindAddress = IPAddress.Parse(tcpOptions.Value.BindAddress);
    private readonly string accessUrl = GVoiceReplies.AccessUrl(options.Value.AdvertisedHost(lobbyAddress.Value), options.Value.Port);
    private Socket? socket;

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

            udp.Bind(new IPEndPoint(bindAddress, options.Value.Port));
        }
        catch
        {
            udp.Dispose();
            throw;
        }

        socket = udp;
        logger.LogInformation("VoiceServer listening on udp {EndPoint}, advertising {AccessUrl}", udp.LocalEndPoint, accessUrl);
        await base.StartAsync(cancellationToken);
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
            await Task.WhenAll(ReceiveUntilStoppedAsync(socket!, stoppingToken), ExpireUntilStoppedAsync(socket!, stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("VoiceServer stopped listening");
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
                logger.LogDebug("Voice receive failed: {Reason}", exception.Message);
                continue;
            }

            if (received.RemoteEndPoint is not IPEndPoint source)
            {
                continue;
            }

            try
            {
                Send(udp, Receive(buffer.AsMemory(0, received.ReceivedBytes), source));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Dropped a voice datagram of {Size} bytes from {Source}", received.ReceivedBytes, source);
            }
        }
    }

    private async Task ExpireUntilStoppedAsync(Socket udp, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ExpiryInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var nowMs = Environment.TickCount64;
            Send(udp, voiceRooms.TakePendingSends());
            Send(udp, voiceRooms.ExpireIdleMembers(nowMs));
            if (nowMs - lastQuietSourceSweepMs >= QuietSourceSweepMs)
            {
                lastQuietSourceSweepMs = nowMs;
                SweepQuietSources(nowMs);
            }
        }
    }

    private void SweepQuietSources(long nowMs)
    {
        getConfigReplies.ForgetFull(nowMs);
        joins.ForgetFull(nowMs);
        joinRefusals.ForgetFull(nowMs);
        voiceRooms.ForgetQuietSources(nowMs);
        if (Interlocked.Exchange(ref unansweredAuthKeyRequests, 0) is > 0 and var authKeyRequests)
        {
            logger.LogDebug("{Count} AV auth key requests in the last {Seconds} s were left unanswered (message and report key)", authKeyRequests, QuietSourceSweepMs / 1000);
        }

        if (voiceRooms.TakeSuppressedRefusalLogs() is > 0 and var suppressed)
        {
            logger.LogWarning("{Count} refused voice datagrams in the last {Seconds} s were not logged (voice log limit)", suppressed, QuietSourceSweepMs / 1000);
        }
    }

    private bool ShouldLogRefusal(IPEndPoint source) => voiceRooms.ShouldLogRefusal(source, Environment.TickCount64);

    private IReadOnlyList<GVoiceSend> Receive(ReadOnlyMemory<byte> datagram, IPEndPoint source)
    {
        var span = datagram.Span;
        if (span.Length > ClientReceiveBufferSize)
        {
            if (ShouldLogRefusal(source))
            {
                logger.LogWarning("Refused a voice datagram of {Size} bytes from {Source}: larger than the client receive buffer", span.Length, source);
            }

            return [];
        }

        if (CDNVProtoRtcp.IsRtcp(span))
        {
            if (!CDNVProtoRtcp.TryParse(span, out var qos))
            {
                if (ShouldLogRefusal(source))
                {
                    logger.LogWarning("Refused a voice RTCP datagram of {Size} bytes from {Source}: not a QosMsg", span.Length, source);
                }

                return [];
            }

            return voiceRooms.HandleRtcp(qos, source, Environment.TickCount64);
        }

        var magic = span.Length >= sizeof(ushort) ? BinaryPrimitives.ReadUInt16BigEndian(span) : (ushort)0;
        switch (magic)
        {
            case AVProtoMessage.Magic:
                return ReceiveAV(datagram, source);
            case CDNVProtoSignal.Magic:
                if (!CDNVProtoSignal.TryParse(datagram, out var signal, out var signalRefusal))
                {
                    if (ShouldLogRefusal(source))
                    {
                        logger.LogWarning("Refused a voice signal datagram of {Size} bytes from {Source}: {Reason}", span.Length, source, signalRefusal);
                    }

                    return [];
                }

                return voiceRooms.HandleSignal(signal, source, Environment.TickCount64);
            case CDNVProtoData.Magic:
                if (!CDNVProtoData.TryParse(span, out var head, out var dataRefusal))
                {
                    if (ShouldLogRefusal(source))
                    {
                        logger.LogWarning("Refused a voice media datagram of {Size} bytes from {Source}: {Reason}", span.Length, source, dataRefusal);
                    }

                    return [];
                }

                return voiceRooms.RelayMedia(head, datagram.ToArray(), source, Environment.TickCount64);
            default:
                logger.LogDebug("Ignored a voice datagram of {Size} bytes from {Source} starting {Magic:x8}", span.Length, source, span.Length >= sizeof(uint) ? BinaryPrimitives.ReadUInt32BigEndian(span) : magic);
                return [];
        }
    }

    private IReadOnlyList<GVoiceSend> ReceiveAV(ReadOnlyMemory<byte> datagram, IPEndPoint source)
    {
        if (!AVProtoMessage.TryParse(datagram, out var message))
        {
            if (ShouldLogRefusal(source))
            {
                logger.LogWarning("Refused an AV datagram of {Size} bytes from {Source}: head or wBody_len is malformed", datagram.Length, source);
            }

            return [];
        }

        if (message.Head.Cmd is AVCmd.GetAuthKeyReq or AVCmd.GetAuthKeyAndWexinIDReq)
        {
            Interlocked.Increment(ref unansweredAuthKeyRequests);
            return [];
        }

        if (message.Head.ProtoVer != AVProtoMessage.PlaintextProtoVer)
        {
            if (ShouldLogRefusal(source))
            {
                logger.LogWarning("Refused AV {Cmd} from {Source}: iProto_ver {ProtoVer} is not the plaintext {Plaintext}", message.Head.Cmd, source, message.Head.ProtoVer, AVProtoMessage.PlaintextProtoVer);
            }

            return [];
        }

        return message.Head.Cmd switch
        {
            AVCmd.JoinRoomReq => JoinRoom(message, source),
            AVCmd.GetConfigReq => GetConfig(message, source),
            _ => IgnoreAV(message, source),
        };
    }

    private IReadOnlyList<GVoiceSend> JoinRoom(AVProtoMessage message, IPEndPoint source)
    {
        if (!message.TryReadJoinRoomReq(out var request))
        {
            if (ShouldLogRefusal(source))
            {
                logger.LogWarning("Refused AV JoinRoomReq from {Source}: body is malformed", source);
            }

            return [];
        }

        var nowMs = Environment.TickCount64;
        var rateLimitKey = GVoiceRoomManager.RateLimitKeyOf(source);
        if (!joins.TryTake(rateLimitKey, nowMs))
        {
            if (ShouldLogRefusal(source))
            {
                logger.LogWarning("Dropped AV JoinRoomReq from {Source}: over {Limit} joins per source", source, JoinsPerSource);
            }

            return [];
        }

        var outcome = voiceRooms.Join(request.OpenId, request.RoomName, request.ClientNetType, source, nowMs);
        if (!outcome.IsAnswered
            || (outcome.ErrCode != GVoiceReplies.ReplySucc && !joinRefusals.TryTake(rateLimitKey, nowMs)))
        {
            return outcome.Pushes;
        }

        if (outcome is { Room: { } joinedRoom, Member: { } joinedMember })
        {
            logger.LogInformation("AV JoinRoomReq seq {Seq} from {Source}: uid {Uid}, room {RoomName}, net type {NetType}, signature {SignatureLength} chars", message.Head.SeqNum, source, joinedMember.Uid, joinedRoom.Name, request.ClientNetType, request.SignatureLength);
        }

        var replyBusinessId = GVoiceReplies.ReplyBusinessId(message.Head.BusinessId);
        var roomInfoJson = outcome is { Room: { } room, Member: { } member }
            ? GVoiceReplies.RoomInfoJson(room, member, message.Head.BusinessId, accessUrl)
            : string.Empty;
        var reply = AVProtoMessage.BuildJoinRoomRsp(message.Head, replyBusinessId, outcome.ErrCode, outcome.ErrMsg, roomInfoJson);
        return [new GVoiceSend(source, reply), .. outcome.Pushes];
    }

    private IReadOnlyList<GVoiceSend> GetConfig(AVProtoMessage message, IPEndPoint source)
    {
        if (!message.TryReadGetConfigReq(out var request)
            || !ulong.TryParse(request.OpenId, NumberStyles.None, CultureInfo.InvariantCulture, out var uid) || uid == 0)
        {
            if (ShouldLogRefusal(source))
            {
                logger.LogWarning("Refused AV GetConfigReq from {Source}: body is malformed or its openid is not a uid", source);
            }

            return [];
        }

        var nowMs = Environment.TickCount64;
        if (!getConfigReplies.TryTake(GVoiceRoomManager.RateLimitKeyOf(source), nowMs) || !allGetConfigReplies.TryTake(AnySource, nowMs))
        {
            if (ShouldLogRefusal(source))
            {
                logger.LogWarning("Dropped AV GetConfigReq from {Source}: over the reply limit ({PerSource} per source, {Global} at once)", source, GetConfigRepliesPerSource, GetConfigRepliesBurst);
            }

            return [];
        }

        logger.LogDebug("AV GetConfigReq seq {Seq} from {Source}: uid {Uid}, signature {SignatureLength} chars", message.Head.SeqNum, source, uid, request.SignatureLength);
        var reply = AVProtoMessage.BuildGetConfigRsp(
            message.Head,
            GVoiceReplies.ReplyBusinessId(message.Head.BusinessId),
            GVoiceReplies.ReplySucc,
            GVoiceReplies.ReplyOk,
            (uint)clock.GetUtcNow().ToUnixTimeSeconds(),
            GVoiceReplies.ConfigInfoJson(message.Head.BusinessId));
        return [new GVoiceSend(source, reply)];
    }

    private IReadOnlyList<GVoiceSend> IgnoreAV(AVProtoMessage message, IPEndPoint source)
    {
        if (ShouldLogRefusal(source))
        {
            logger.LogInformation("AV {Cmd} seq {Seq} from {Source} has no reply", message.Head.Cmd, message.Head.SeqNum, source);
        }

        return [];
    }

    private void Send(Socket udp, IReadOnlyList<GVoiceSend> sends)
    {
        foreach (var send in sends)
        {
            try
            {
                udp.SendTo(send.Datagram, SocketFlags.None, send.Target);
            }
            catch (SocketException exception)
            {
                logger.LogDebug("Voice send to {Target} failed: {Reason}", send.Target, exception.Message);
            }
        }
    }
}
