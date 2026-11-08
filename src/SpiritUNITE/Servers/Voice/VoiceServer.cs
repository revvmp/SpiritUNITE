using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Game.BattleVoice;
using SpiritUNITE.Network;
using SpiritUNITE.Network.GVoice;
using SpiritUNITE.Servers.Lobby;

namespace SpiritUNITE.Servers.Voice;

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
    private static readonly TimeSpan ExpiryInterval = TimeSpan.FromSeconds(1);
    private static readonly byte[] ConnectionResetReportsOff = [0, 0, 0, 0];

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
            logger.LogInformation("Stopped listening");
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
            Send(udp, voiceRooms.ExpireIdleMembers(Environment.TickCount64));
        }
    }

    private IReadOnlyList<GVoiceSend> Receive(ReadOnlyMemory<byte> datagram, IPEndPoint source)
    {
        var span = datagram.Span;
        if (span.Length > ClientReceiveBufferSize)
        {
            logger.LogWarning("Refused a voice datagram of {Size} bytes from {Source}: larger than the client receive buffer", span.Length, source);
            return [];
        }

        if (CDNVProtoRtcp.IsRtcp(span))
        {
            if (!CDNVProtoRtcp.TryParse(span, out var qos))
            {
                logger.LogWarning("Refused a voice RTCP datagram of {Size} bytes from {Source}: not a QosMsg", span.Length, source);
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
                    logger.LogWarning("Refused a voice signal datagram of {Size} bytes from {Source}: {Reason}", span.Length, source, signalRefusal);
                    return [];
                }

                return voiceRooms.HandleSignal(signal, source, Environment.TickCount64);
            case CDNVProtoData.Magic:
                if (!CDNVProtoData.TryParse(span, out var head, out var dataRefusal))
                {
                    logger.LogWarning("Refused a voice media datagram of {Size} bytes from {Source}: {Reason}", span.Length, source, dataRefusal);
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
            logger.LogWarning("Refused an AV datagram of {Size} bytes from {Source}: head or wBody_len is malformed", datagram.Length, source);
            return [];
        }

        if (message.Head.ProtoVer != AVProtoMessage.PlaintextProtoVer)
        {
            logger.LogWarning("Refused AV {Cmd} from {Source}: iProto_ver {ProtoVer} is not the plaintext {Plaintext}", message.Head.Cmd, source, message.Head.ProtoVer, AVProtoMessage.PlaintextProtoVer);
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
            logger.LogWarning("Refused AV JoinRoomReq from {Source}: body is malformed", source);
            return [];
        }

        logger.LogInformation("AV JoinRoomReq seq {Seq} from {Source}: openid {OpenId}, room {RoomName}, net type {NetType}, signature {SignatureLength} chars", message.Head.SeqNum, source, request.OpenId, request.RoomName, request.ClientNetType, request.SignatureLength);
        var outcome = voiceRooms.Join(request.OpenId, request.RoomName, request.ClientNetType, Environment.TickCount64);
        var replyBusinessId = GVoiceReplies.ReplyBusinessId(message.Head.BusinessId);
        var roomInfoJson = outcome is { Room: { } room, Member: { } member }
            ? GVoiceReplies.RoomInfoJson(room, member, message.Head.BusinessId, accessUrl)
            : string.Empty;
        var reply = AVProtoMessage.BuildJoinRoomRsp(message.Head, replyBusinessId, outcome.ErrCode, outcome.ErrMsg, roomInfoJson);
        return [new GVoiceSend(source, reply), .. outcome.Pushes];
    }

    private IReadOnlyList<GVoiceSend> GetConfig(AVProtoMessage message, IPEndPoint source)
    {
        if (!message.TryReadGetConfigReq(out var request))
        {
            logger.LogWarning("Refused AV GetConfigReq from {Source}: body is malformed", source);
            return [];
        }

        logger.LogInformation("AV GetConfigReq seq {Seq} from {Source}: openid {OpenId}, signature {SignatureLength} chars", message.Head.SeqNum, source, request.OpenId, request.SignatureLength);
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
        logger.LogInformation("AV {Cmd} seq {Seq} from {Source} has no reply", message.Head.Cmd, message.Head.SeqNum, source);
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
