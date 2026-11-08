using System.Collections.Frozen;
using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUnite.Auth;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Network.Gcp;

public sealed class GcpSession(
    eNetEventServerType serverType,
    Func<ulong, GamePacket[]> greeting,
    PacketDispatcher dispatcher,
    Func<GcpAuthReq, Task<ulong>> authenticate,
    Func<byte[], Task> write,
    Action close,
    GcpSessionHooks hooks,
    GcpCompression compression,
    ILogger logger) : IAsyncDisposable
{
    private const int MaxLoggedBytes = 64;
    private const int NoClientSeq = 0;
    private const int NoStopExCode = 0;

    private static readonly FrozenSet<int> PacketsWithoutIsPkgMem = new[]
    {
        PB_ENUM_CS_MSGID_DEF.PbScidOffingRestartNtf,
        PB_ENUM_CS_MSGID_DEF.PbScidFightHistoryListRsp,
        PB_ENUM_CS_MSGID_DEF.PbScidMultGameBeginPickNtf,
        PB_ENUM_CS_MSGID_DEF.PbScidChooseHeroRsp,
        PB_ENUM_CS_MSGID_DEF.PbScidLockHeroRsp,
        PB_ENUM_CS_MSGID_DEF.PbScidChoiceHeroChgNtf,
        PB_ENUM_CS_MSGID_DEF.PbScidDeskUpdPickWaitTimeNtf,
        PB_ENUM_CS_MSGID_DEF.PbScidConfirmHeroRsp,
    }.Select(msgID => (int)msgID).ToFrozenSet();

    private readonly PacketLog packetLog = new(logger);
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private readonly SemaphoreSlim requestLock = new(1, 1);
    private readonly LoginSession login = new();
    private readonly byte sessionRoute = (byte)RandomNumberGenerator.GetInt32(byte.MaxValue + 1);
    private GcpCipher? cipher;
    private GcpHead? lastHead;
    private bool loginCompletedNotified;
    private bool authenticationStarted;
    private GcpSessionState state = GcpSessionState.AwaitingSyn;
    private uint nextSequence;
    private int nextSvrPkgSeq = 1;
    private ulong gameAcntUid;
    private GcpSessionStyle style = GcpSessionStyle.Lobby;
    private ReadOnlyMemory<byte> synAccount;
    private ulong authenticatedUid;
    private int lifetime = (int)SessionLifetime.Open;
    private int latestClientSeq;

    internal int NextSvrPkgSeq => nextSvrPkgSeq;

    public uint? SynReserved { get; private set; }

    public Func<byte[], Task>? WriteUnreliable { get; init; }

    public Func<TimeSpan?>? MeasureRtt { get; init; }

    public TimeSpan? Rtt => MeasureRtt?.Invoke();

    public GcpSessionStyle Style
    {
        get => style;
        init
        {
            style = value;
            nextSequence = value.FirstHeadSequence;
        }
    }

    public bool AwaitingRegistration => serverType == eNetEventServerType.LobbyServer && gameAcntUid != 0 && login.Phase != LoginPhase.Completed;

    internal bool IsOpen => Volatile.Read(ref lifetime) == (int)SessionLifetime.Open;

    private enum SessionLifetime
    {
        Open,
        Closing,
        Disposed,
    }

    private enum GcpSessionState
    {
        AwaitingSyn,
        AwaitingAuth,
        Established,
    }

    public async Task HandleAsync(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        await requestLock.WaitAsync();
        try
        {
            ThrowIfClosed();
            await HandlePacketAsync(head, packet);
        }
        finally
        {
            requestLock.Release();
        }
    }

    private async Task HandlePacketAsync(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        packetLog.Received(head);
        lastHead = head;

        var task = (state, (GcpCommand)head.Command) switch
        {
            (GcpSessionState.AwaitingSyn, GcpCommand.Syn) => SendAsync(() => AcceptSyn(head, packet)),
            (GcpSessionState.AwaitingAuth, GcpCommand.AuthReq) => AcceptAuthAsync(head, packet),
            (GcpSessionState.Established, GcpCommand.Data) => AcceptDataAsync(head, packet),
            (GcpSessionState.Established, GcpCommand.ClientStop) when style.AnswersClientStop => AcceptClientStopAsync(),
            (GcpSessionState.Established, _) => IgnoreEstablishedPacket(head),
            _ => throw new GcpProtocolException($"Command 0x{head.Command:X4} is not valid in state {state}"),
        };
        await task;
        await NotifyLoginCompletedAsync();
    }

    public async Task HandleUnreliableBodyAsync(ReadOnlyMemory<byte> encryptedBody, bool compressed)
    {
        await requestLock.WaitAsync();
        try
        {
            ThrowIfClosed();
            await AcceptUnreliableBodyAsync(encryptedBody, compressed);
        }
        finally
        {
            requestLock.Release();
        }
    }

    private async Task AcceptUnreliableBodyAsync(ReadOnlyMemory<byte> encryptedBody, bool compressed)
    {
        if (lastHead is not { } head || state != GcpSessionState.Established)
        {
            throw new GcpProtocolException("Unreliable data arrived before the session was established");
        }

        var replies = await DispatchGamePacketAsync(ReadClientPayload(encryptedBody.Span, compressed));
        await SendAsync(() => [.. replies.Select(reply => BuildGamePacket(head, reply))]);
    }

    public async Task PushAsync(IReadOnlyList<GamePacket> packets)
    {
        if (lastHead is not { } head || state != GcpSessionState.Established)
        {
            throw new InvalidOperationException("Session cannot take pushed packets before it is established");
        }

        await SendAsync(() => [.. packets.Select(packet => BuildGamePacket(head, StampClientSeq(packet, Volatile.Read(ref latestClientSeq))))]);
    }

    public async Task PushUnreliableAsync(IReadOnlyList<GamePacket> packets)
    {
        if (lastHead is not { } head || state != GcpSessionState.Established)
        {
            throw new InvalidOperationException("Session cannot take pushed packets before it is established");
        }

        await SendAsync(() => [.. packets.Select(packet => BuildGamePacket(head, StampClientSeq(packet, Volatile.Read(ref latestClientSeq))))], WriteUnreliable ?? write);
    }

    public void Close()
    {
        if (Interlocked.CompareExchange(ref lifetime, (int)SessionLifetime.Closing, (int)SessionLifetime.Open) == (int)SessionLifetime.Open)
        {
            try
            {
                close();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Closing the GCP transport failed; session cleanup will continue");
            }
        }
    }

    public async Task StopAsync(GcpStopReason reason, int exCode)
    {
        try
        {
            if (lastHead is { } head && state == GcpSessionState.Established)
            {
                logger.LogInformation("Stopping the GCP session: reason {Reason}, ex code 0x{ExCode:X8}", reason, exCode);
                await SendAsync(() => [BuildPacket(head, GcpCommand.ServerStop, [], GcpMessages.BuildServerStop(reason, exCode), encryptsBody: false)]);
            }
        }
        finally
        {
            Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Close();
        await requestLock.WaitAsync();
        try
        {
            if (Volatile.Read(ref lifetime) == (int)SessionLifetime.Disposed)
            {
                return;
            }

            try
            {
                if (loginCompletedNotified)
                {
                    await CloseHookAsync(hooks.SessionClosed, gameAcntUid);
                }

                if (authenticationStarted)
                {
                    await CloseHookAsync(hooks.AuthenticatedSessionClosed, authenticatedUid);
                }
            }
            finally
            {
                await sendLock.WaitAsync();
                try
                {
                    cipher?.Dispose();
                    cipher = null;
                }
                finally
                {
                    Volatile.Write(ref lifetime, (int)SessionLifetime.Disposed);
                    sendLock.Release();
                }
            }
        }
        finally
        {
            requestLock.Release();
        }
    }

    private async Task CloseHookAsync(Func<ulong, GcpSession, Task> hook, ulong uid)
    {
        try
        {
            await hook(uid, this);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Session cleanup failed for uid {Uid}", uid);
        }
    }

    private void ThrowIfClosed() => ObjectDisposedException.ThrowIf(!IsOpen, this);

    private async Task NotifyLoginCompletedAsync()
    {
        if (loginCompletedNotified || login.Phase != LoginPhase.Completed || gameAcntUid == 0)
        {
            return;
        }

        loginCompletedNotified = true;
        await hooks.LoginCompleted(gameAcntUid, this);
    }

    private Task SendAsync(Func<byte[][]> build) => SendAsync(build, write);

    private async Task SendAsync(Func<byte[][]> build, Func<byte[], Task> writer)
    {
        await sendLock.WaitAsync();
        try
        {
            ThrowIfClosed();
            foreach (var packet in build())
            {
                await writer(packet);
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

        var syn = GcpMessages.ParseSyn(section);
        SynReserved = syn.Reserved;
        synAccount = style.EchoesAccountInAuthRsp ? syn.Account : ReadOnlyMemory<byte>.Empty;
        var sessionKey = RandomNumberGenerator.GetBytes(GcpCipher.KeySize);
        cipher = new GcpCipher(sessionKey);
        state = GcpSessionState.AwaitingAuth;

        return [BuildPacket(head, GcpCommand.Ack, GcpMessages.BuildAckSection(sessionKey, head.PackingVersion, compression), GcpMessages.BuildAckBody())];
    }

    private async Task AcceptAuthAsync(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        var body = cipher!.Decrypt(packet.Span[(int)head.HeadLength..]);
        var request = GcpMessages.ParseAuthReq(body, head.HeaderVersion);
        logger.LogInformation("GCP auth type {AuthType}, token {TokenLength} bytes, trans data {TransDataLength} bytes",
            request.AuthType, request.Token.Length, request.TransData.Length);
        LogTransData(request.TransData.Span);
        authenticatedUid = await authenticate(request);

        if (authenticatedUid != 0)
        {
            authenticationStarted = true;
            await hooks.Authenticating(authenticatedUid, this);
            ThrowIfClosed();
        }

        state = GcpSessionState.Established;
        var transDataReplies = request.TransData.IsEmpty
            ? []
            : await DispatchGamePacketAsync(request.TransData);
        var greetingPackets = greeting(authenticatedUid);
        ContinueSvrPkgSeqUnlessReset(greetingPackets);
        await SendAsync(() =>
        [
            BuildPacket(head, GcpCommand.AuthRsp, [], BuildAuthRsp(head.HeaderVersion)),
            BuildPacket(head, GcpCommand.Bingo, [], BuildBingo(head.HeaderVersion)),
            .. greetingPackets.Select(packet => BuildGamePacket(head, packet)),
            .. transDataReplies.Select(packet => BuildGamePacket(head, packet)),
        ]);
        if (authenticatedUid != 0)
        {
            await hooks.Authenticated(authenticatedUid, this);
        }
    }

    private byte[] BuildAuthRsp(int headerVersion) => style.EchoesAccountInAuthRsp
        ? GcpMessages.BuildAccountAuthRsp(headerVersion, synAccount.Span)
        : GcpMessages.BuildEmptyAuthRsp(headerVersion);

    private byte[] BuildBingo(int headerVersion) => style.SendsBingoSession
        ? GcpMessages.BuildSessionBingo(headerVersion, compression)
        : GcpMessages.BuildBingo(headerVersion, compression);

    private Task AcceptClientStopAsync()
    {
        logger.LogInformation("Client stopped the GCP session");
        return StopAsync(GcpStopReason.GameServerClose, NoStopExCode);
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
        var compressed = GcpMessages.IsCompressedData(packet.Span[GcpHead.Size..(int)head.HeadLength]);
        var replies = await DispatchGamePacketAsync(ReadClientPayload(packet.Span[(int)head.HeadLength..], compressed));
        await SendAsync(() => [.. replies.Select(reply => BuildGamePacket(head, reply))]);
    }

    private ReadOnlyMemory<byte> ReadClientPayload(ReadOnlySpan<byte> encryptedBody, bool compressed)
    {
        var data = GcpMessages.ReadClientData(cipher!.Decrypt(encryptedBody));
        return compression.Compresses && compressed ? Lz4Block.Decompress(data.Span, GcpHead.MaxClientMessageSize) : data;
    }

    private async Task<GamePacket[]> DispatchGamePacketAsync(ReadOnlyMemory<byte> data)
    {
        ThrowIfClosed();
        var request = ParseGamePacket(data) with { Uid = gameAcntUid != 0 ? gameAcntUid : authenticatedUid, Login = login };
        packetLog.ReceivedGame(request);
        if (request.ClientSeq != NoClientSeq)
        {
            Volatile.Write(ref latestClientSeq, request.ClientSeq);
        }

        try
        {
            RequireLoginState(request);
            if (await dispatcher.DispatchAsync(serverType, request) is { } replies)
            {
                gameAcntUid = replies.FirstOrDefault(reply => reply.Uid != 0)?.Uid ?? gameAcntUid;
                if (gameAcntUid != 0)
                {
                    await hooks.RepliesSending(gameAcntUid, replies);
                }

                return [.. replies.Select(reply => StampClientSeq(reply, request.ClientSeq))];
            }
        }
        catch (Exception exception) when (exception is InvalidProtocolBufferException or InvalidDataException)
        {
            throw new GcpProtocolException($"Message {request.MsgID} was rejected: {exception.Message}");
        }

        packetLog.Unhandled(serverType, request);
        return serverType == eNetEventServerType.LobbyServer && DefaultReplies.TryBuild(request) is { } defaultReply
            ? [StampClientSeq(defaultReply, request.ClientSeq)]
            : [];
    }

    private void RequireLoginState(GamePacket request)
    {
        if (serverType != eNetEventServerType.LobbyServer || login.AllowsGameplay)
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

    private static GamePacket StampClientSeq(GamePacket packet, int clientSeq) =>
        packet.ClientSeq == NoClientSeq && clientSeq != NoClientSeq ? packet with { ClientSeq = clientSeq } : packet;

    private bool WritesIsPkgMem(GamePacket packet) =>
        serverType == eNetEventServerType.LobbyServer && !PacketsWithoutIsPkgMem.Contains(packet.MsgID);

    private byte[] BuildGamePacket(GcpHead head, GamePacket packet)
    {
        packetLog.SentGame(packet);
        var payload = style.WritesPkgHeadUidAndSvrPkgSeq
            ? packet.ToByteArray(nextSvrPkgSeq++, WritesIsPkgMem(packet))
            : packet.ToRelayByteArray();
        var compressed = compression.Compresses && payload.Length >= compression.MinSize ? Lz4Block.Compress(payload) : null;
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

    private byte[] BuildPacket(GcpHead request, GcpCommand command, ReadOnlySpan<byte> section, ReadOnlySpan<byte> body, bool encryptsBody = true)
    {
        var encrypted = encryptsBody && cipher is not null && !body.IsEmpty;
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
