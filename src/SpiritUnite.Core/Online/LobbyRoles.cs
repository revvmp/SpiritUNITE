using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using SpiritUnite.Network.Gcp;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Online;

public sealed class LobbyRoles(ILogger<LobbyRoles> logger)
{
    private const int KickExCode = 0;

    private static readonly TimeSpan KickOffPushTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<ulong, GcpSession> sessions = new();
    private readonly SessionOwnership ownership = new();
    private readonly ConcurrentDictionary<ulong, int> closedNextSvrPkgSeqs = new();
    private readonly Lock presenceTurnGate = new();
    private readonly Dictionary<ulong, Task> presenceTurns = [];

    public event Func<ulong, Task>? RoleOnline;

    public event Func<ulong, Task>? RoleOffline;

    public event Func<ulong, Task>? FreshLogin;

    public event Func<ulong, IReadOnlyList<GamePacket>, Task>? PacketsSending;

    public bool IsOnline(ulong uid) => sessions.ContainsKey(uid);

    public IReadOnlyList<ulong> OnlineUids => [.. sessions.Keys];

    public TimeSpan? Rtt(ulong uid) => sessions.TryGetValue(uid, out var session) ? session.Rtt : null;

    public IPAddress? RemoteAddress(ulong uid) => sessions.TryGetValue(uid, out var session) ? session.RemoteAddress : null;

    public async Task<bool> PushAsync(ulong uid, params GamePacket[] packets)
    {
        if (packets.Length == 0 || !sessions.TryGetValue(uid, out var session))
        {
            return false;
        }

        try
        {
            await RaiseSendingAsync(uid, packets);
            await session.PushAsync([.. packets.Select(packet => packet with { Uid = uid })]);
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogInformation("Push to uid {Uid} failed: {Reason}", uid, exception.Message);
            return false;
        }
    }

    public async Task<bool> KickAsync(ulong uid, PB_ENUM_RES_ERROR_CODE? kickOffResult = null)
    {
        if (!sessions.TryGetValue(uid, out var session))
        {
            return false;
        }

        logger.LogInformation("Uid {Uid} kicked from the lobby", uid);
        if (kickOffResult is { } result)
        {
            await PushKickOffAsync(uid, session, result);
        }

        try
        {
            await session.StopAsync(GcpStopReason.GameServerClose, KickExCode);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogInformation("Kick of uid {Uid} could not send the stop: {Reason}", uid, exception.Message);
        }

        session.Close();
        return true;
    }

    internal Task AttachAsync(ulong uid, GcpSession session) => InPresenceTurnAsync(uid, () => AttachInTurnAsync(uid, session));

    private async Task AttachInTurnAsync(ulong uid, GcpSession session)
    {
        if (!ownership.TryUse(uid, session, () => sessions[uid] = session))
        {
            logger.LogInformation("Uid {Uid} finished login on a superseded lobby session; it stays offline", uid);
            return;
        }

        logger.LogInformation("Uid {Uid} is online", uid);
        await RaiseAsync(RoleOnline, uid);
    }

    internal async Task ClaimAsync(ulong uid, GcpSession session)
    {
        if (await ownership.ClaimAsync(uid, session, previous => PushKickOffAsync(uid, previous, PB_ENUM_RES_ERROR_CODE.PbErrDuplicateLogin)))
        {
            logger.LogInformation("Uid {Uid} replaced its earlier lobby session", uid);
        }
    }

    public Task BeginFreshLoginAsync(ulong uid) => InPresenceTurnAsync(uid, () => RaiseAsync(FreshLogin, uid));

    private async Task PushKickOffAsync(ulong uid, GcpSession session, PB_ENUM_RES_ERROR_CODE result)
    {
        var kickOff = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbCsidKickOffPlayerNtf, new pbCSKickOffPlayerNtf
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        });
        try
        {
            await session.PushAsync([kickOff with { Uid = uid }]).WaitAsync(KickOffPushTimeout);
            logger.LogInformation("Uid {Uid} sent KickOffPlayerNtf {Result}", uid, result);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException or TimeoutException)
        {
            logger.LogInformation("KickOffPlayerNtf to uid {Uid} could not be sent: {Reason}", uid, exception.Message);
        }
    }

    internal Task ReleaseAsync(ulong uid, GcpSession session) => InPresenceTurnAsync(uid, () => ReleaseInTurnAsync(uid, session));

    private async Task ReleaseInTurnAsync(ulong uid, GcpSession session)
    {
        if (ownership.Release(uid, session)
            && sessions.TryGetValue(uid, out var abandoned)
            && !abandoned.IsOpen
            && sessions.TryRemove(new KeyValuePair<ulong, GcpSession>(uid, abandoned)))
        {
            closedNextSvrPkgSeqs[uid] = abandoned.NextSvrPkgSeq;
            logger.LogInformation("Uid {Uid} is offline", uid);
            await RaiseAsync(RoleOffline, uid);
        }
    }

    internal int? FindNextSvrPkgSeq(ulong uid) =>
        sessions.TryGetValue(uid, out var session) ? session.NextSvrPkgSeq
        : closedNextSvrPkgSeqs.TryGetValue(uid, out var closedNextSvrPkgSeq) ? closedNextSvrPkgSeq
        : null;

    internal Task DetachAsync(ulong uid, GcpSession session) => InPresenceTurnAsync(uid, () => DetachInTurnAsync(uid, session));

    private async Task DetachInTurnAsync(ulong uid, GcpSession session)
    {
        if (!ownership.IsCurrent(uid, session) || !sessions.TryRemove(new KeyValuePair<ulong, GcpSession>(uid, session)))
        {
            return;
        }

        closedNextSvrPkgSeqs[uid] = session.NextSvrPkgSeq;
        logger.LogInformation("Uid {Uid} is offline", uid);
        await RaiseAsync(RoleOffline, uid);
    }

    internal async Task RaiseSendingAsync(ulong uid, IReadOnlyList<GamePacket> packets)
    {
        if (PacketsSending is not { } handlers || packets.Count == 0)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<ulong, IReadOnlyList<GamePacket>, Task>>())
        {
            try
            {
                await handler(uid, packets);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Sending handler failed for uid {Uid}", uid);
            }
        }
    }

    private async Task InPresenceTurnAsync(ulong uid, Func<Task> transition)
    {
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previousTurn;
        lock (presenceTurnGate)
        {
            previousTurn = presenceTurns.GetValueOrDefault(uid, Task.CompletedTask);
            presenceTurns[uid] = turn.Task;
        }

        try
        {
            await previousTurn;
            await transition();
        }
        finally
        {
            lock (presenceTurnGate)
            {
                if (presenceTurns.GetValueOrDefault(uid) == turn.Task)
                {
                    presenceTurns.Remove(uid);
                }
            }

            turn.SetResult();
        }
    }

    private async Task RaiseAsync(Func<ulong, Task>? handlers, ulong uid)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<ulong, Task>>())
        {
            try
            {
                await handler(uid);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Presence handler failed for uid {Uid}", uid);
            }
        }
    }
}
