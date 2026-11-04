using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Network.Gcp;

public sealed class GcpSession(
    eNetEventServerType serverType,
    Func<ulong, GamePacket[]> greeting,
    PacketDispatcher dispatcher,
    Func<GcpAuthReq, Task<ulong>> authenticate,
    Func<byte[], Task> write,
    GcpSessionHooks hooks,
    ILogger logger) : IAsyncDisposable
{
    private const int MaxLoggedBytes = 64;

    private readonly PacketLog packetLog = new(logger);
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly byte sessionRoute = (byte)RandomNumberGenerator.GetInt32(byte.MaxValue + 1);
    private GcpCipher? cipher;
    private GcpHead? lastHead;
    private bool loginCompletedNotified;
    private GcpSessionState state = GcpSessionState.AwaitingSyn;
    private uint nextSequence;
    private int nextSvrPkgSeq = 1;
    private ulong gameAcntUid;
    private ulong authenticatedUid;
    private bool loginCompleted;
    private bool acntLoginSent;

    internal int NextSvrPkgSeq => nextSvrPkgSeq;

    public bool AwaitingRegistration => serverType == eNetEventServerType.LobbyServer && gameAcntUid != 0 && !loginCompleted;

    private enum GcpSessionState
    {
        AwaitingSyn,
        AwaitingAuth,
        Established,
    }

    public async Task HandleAsync(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        packetLog.Received(head);
        lastHead = head;

        var task = (state, (GcpCommand)head.Command) switch
        {
            (GcpSessionState.AwaitingSyn, GcpCommand.Syn) => SendAsync(() => AcceptSyn(head, packet)),
            (GcpSessionState.AwaitingAuth, GcpCommand.AuthReq) => AcceptAuthAsync(head, packet),
            (GcpSessionState.Established, GcpCommand.Data) => AcceptDataAsync(head, packet),
            (GcpSessionState.Established, _) => IgnoreEstablishedPacket(head),
            _ => throw new GcpProtocolException($"Command 0x{head.Command:X4} is not valid in state {state}"),
        };
        await task;
        await NotifyLoginCompletedAsync();
    }

    public async Task PushAsync(IReadOnlyList<GamePacket> packets)
    {
        if (lastHead is not { } head || state != GcpSessionState.Established)
        {
            throw new InvalidOperationException("Session cannot take pushed packets before it is established");
        }

        await SendAsync(() => [.. packets.Select(packet => BuildGamePacket(head, packet))]);
    }

    public async ValueTask DisposeAsync()
    {
        if (loginCompletedNotified)
        {
            await hooks.SessionClosed(gameAcntUid, this);
        }

        await sendLock.WaitAsync();
        cipher?.Dispose();
        cipher = null;
        sendLock.Release();
    }

    private async Task NotifyLoginCompletedAsync()
    {
        if (loginCompletedNotified || !loginCompleted || gameAcntUid == 0)
        {
            return;
        }

        loginCompletedNotified = true;
        await hooks.LoginCompleted(gameAcntUid, this);
    }

    private async Task SendAsync(Func<byte[][]> build)
    {
        await sendLock.WaitAsync();
        try
        {
            foreach (var packet in build())
            {
                await write(packet);
            }
        }
        finally
        {
            sendLock.Release();
        }
    }

    private byte[][] AcceptSyn(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        var section = packet[GcpHead.Size..(int)head.HeadLength];
        if (section.IsEmpty || section.Span[0] != (byte)KeyMaking.Server)
        {
            throw new GcpProtocolException("SYN does not request a server generated key");
        }

        var sessionKey = RandomNumberGenerator.GetBytes(GcpCipher.KeySize);
        cipher = new GcpCipher(sessionKey);
        state = GcpSessionState.AwaitingAuth;

        return [BuildPacket(head, GcpCommand.Ack, GcpMessages.BuildAckSection(sessionKey, head.PackingVersion), GcpMessages.BuildAckBody())];
    }

    private async Task AcceptAuthAsync(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        var body = cipher!.Decrypt(packet.Span[(int)head.HeadLength..]);
        var request = GcpMessages.ParseAuthReq(body, head.HeaderVersion);
        logger.LogInformation("GCP auth type {AuthType}, token {TokenLength} bytes, trans data {TransDataLength} bytes",
            request.AuthType, request.Token.Length, request.TransData.Length);
        LogTransData(request.TransData.Span);
        authenticatedUid = await authenticate(request);

        state = GcpSessionState.Established;
        var transDataReplies = request.TransData.IsEmpty
            ? []
            : await DispatchGamePacketAsync(request.TransData);
        var greetingPackets = greeting(authenticatedUid);
        ContinueSvrPkgSeqUnlessReset(greetingPackets);
        await SendAsync(() =>
        [
            BuildPacket(head, GcpCommand.AuthRsp, [], GcpMessages.BuildEmptyAuthRsp(head.HeaderVersion)),
            BuildPacket(head, GcpCommand.Bingo, [], GcpMessages.BuildBingo(head.HeaderVersion)),
            .. greetingPackets.Select(packet => BuildGamePacket(head, packet)),
            .. transDataReplies.Select(packet => BuildGamePacket(head, packet)),
        ]);
    }

    private void ContinueSvrPkgSeqUnlessReset(GamePacket[] greetingPackets)
    {
        if (greetingPackets.Length == 0 || greetingPackets.Any(packet => packet.MsgID == (int)PB_ENUM_CS_MSGID_DEF.PbScidLoginNowNtf))
        {
            return;
        }

        nextSvrPkgSeq = hooks.FindNextSvrPkgSeq(authenticatedUid) ?? nextSvrPkgSeq;
    }

    private async Task AcceptDataAsync(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        var body = cipher!.Decrypt(packet.Span[(int)head.HeadLength..]);
        var data = GcpMessages.ReadClientData(body);
        if (GcpMessages.IsCompressedData(packet.Span[GcpHead.Size..(int)head.HeadLength]))
        {
            data = Lz4Block.Decompress(data.Span, GcpHead.MaxPacketSize);
        }

        var replies = await DispatchGamePacketAsync(data);
        await SendAsync(() => [.. replies.Select(reply => BuildGamePacket(head, reply))]);
    }

    private async Task<GamePacket[]> DispatchGamePacketAsync(ReadOnlyMemory<byte> data)
    {
        var request = ParseGamePacket(data) with { Uid = gameAcntUid != 0 ? gameAcntUid : authenticatedUid };
        packetLog.ReceivedGame(request);

        try
        {
            RequireLoginState(request);
            if (await dispatcher.DispatchAsync(serverType, request) is { } replies)
            {
                gameAcntUid = replies.FirstOrDefault(reply => reply.Uid != 0)?.Uid ?? gameAcntUid;
                acntLoginSent |= replies.Any(reply => reply.MsgID == (int)PB_ENUM_CS_MSGID_DEF.PbScidAcntLoginRsp && reply.Uid != 0);
                loginCompleted |= replies.Any(CompletesLogin);
                if (gameAcntUid != 0)
                {
                    await hooks.RepliesSending(gameAcntUid, replies);
                }

                return replies;
            }
        }
        catch (Exception exception) when (exception is InvalidProtocolBufferException or InvalidDataException)
        {
            throw new GcpProtocolException($"Message {request.MsgID} was rejected: {exception.Message}");
        }

        packetLog.Unhandled(serverType, request);
        return serverType == eNetEventServerType.LobbyServer && DefaultReplies.TryBuild(request) is { } defaultReply
            ? [defaultReply]
            : [];
    }

    private void RequireLoginState(GamePacket request)
    {
        if (serverType != eNetEventServerType.LobbyServer || loginCompleted || acntLoginSent)
        {
            return;
        }

        var message = (PB_ENUM_CS_MSGID_DEF)request.MsgID;
        if (message is PB_ENUM_CS_MSGID_DEF.PbCsidAcntLoginReq or PB_ENUM_CS_MSGID_DEF.PbCsidOffingRestartReq
            or PB_ENUM_CS_MSGID_DEF.PbCsidGameSvrPing or PB_ENUM_CS_MSGID_DEF.PbCsidReconnectInfoReport)
        {
            return;
        }

        if (gameAcntUid != 0 && message is PB_ENUM_CS_MSGID_DEF.PbCsidAcntRegisterReq or PB_ENUM_CS_MSGID_DEF.PbCsidClientReportTlogReq)
        {
            return;
        }

        throw new InvalidDataException("Message requires completed trainer registration and login");
    }

    private static bool CompletesLogin(GamePacket reply) =>
        reply.MsgID == (int)PB_ENUM_CS_MSGID_DEF.PbScidLoginFinishNtf
        || reply.MsgID == (int)PB_ENUM_CS_MSGID_DEF.PbScidOffingRestartRsp && reply.Uid != 0;

    private byte[] BuildGamePacket(GcpHead head, GamePacket packet)
    {
        packetLog.SentGame(packet);
        var payload = packet.ToByteArray(nextSvrPkgSeq++);
        var compressed = payload.Length >= GcpMessages.CompressMinSize ? Lz4Block.Compress(payload) : null;
        return compressed is not null && compressed.Length < payload.Length
            ? BuildPacket(head, GcpCommand.Data, GcpMessages.BuildDataSection(head.PackingVersion, compressed: true, sessionRoute), compressed)
            : BuildPacket(head, GcpCommand.Data, GcpMessages.BuildDataSection(head.PackingVersion, compressed: false, sessionRoute), payload);
    }

    private static GamePacket ParseGamePacket(ReadOnlyMemory<byte> data)
    {
        try
        {
            return GamePacket.Parse(data);
        }
        catch (InvalidDataException exception)
        {
            throw new GcpProtocolException(exception.Message);
        }
    }

    private Task IgnoreEstablishedPacket(GcpHead head)
    {
        logger.LogInformation("Ignoring command 0x{Command:X4}: nothing handles it yet", head.Command);
        return Task.CompletedTask;
    }

    private void LogTransData(ReadOnlySpan<byte> transData)
    {
        var loggedBytes = transData[..Math.Min(transData.Length, MaxLoggedBytes)];
        logger.LogDebug("Trans data begins {Hex}", Convert.ToHexString(loggedBytes));
    }

    private byte[] BuildPacket(GcpHead request, GcpCommand command, ReadOnlySpan<byte> section, ReadOnlySpan<byte> body)
    {
        var encrypted = cipher is not null && !body.IsEmpty;
        ReadOnlySpan<byte> sentBody = encrypted ? cipher!.Encrypt(body) : body;
        var headLength = GcpHead.Size + section.Length;
        var packet = new byte[headLength + sentBody.Length];
        var head = new GcpHead(
            request.PackingVersion,
            request.HeaderVersion,
            (ushort)command,
            encrypted,
            nextSequence++,
            (uint)headLength,
            (uint)sentBody.Length);

        head.WriteTo(packet);
        section.CopyTo(packet.AsSpan(GcpHead.Size));
        sentBody.CopyTo(packet.AsSpan(headLength));
        packetLog.Sent(head);
        return packet;
    }
}
