using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpiritUnite.Network.Gcp;
using SpiritUnite.Packets;

namespace SpiritUnite.Online;

public sealed class LobbyRoles(ILogger<LobbyRoles> logger)
{
    private const int KickExCode = 0;

    private readonly ConcurrentDictionary<ulong, GcpSession> sessions = new();
    private readonly SessionOwnership ownership = new();
    private readonly ConcurrentDictionary<ulong, int> closedNextSvrPkgSeqs = new();

    public event Func<ulong, Task>? RoleOnline;

    public event Func<ulong, Task>? RoleOffline;

    public event Func<ulong, IReadOnlyList<GamePacket>, Task>? PacketsSending;

    public bool IsOnline(ulong uid) => sessions.ContainsKey(uid);

    public IReadOnlyList<ulong> OnlineUids => [.. sessions.Keys];

    public TimeSpan? Rtt(ulong uid) => sessions.TryGetValue(uid, out var session) ? session.Rtt : null;

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

    public async Task<bool> KickAsync(ulong uid)
    {
        if (!sessions.TryGetValue(uid, out var session))
        {
            return false;
        }

        logger.LogInformation("Uid {Uid} kicked from the lobby", uid);
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

    internal async Task AttachAsync(ulong uid, GcpSession session)
    {
        ownership.Use(uid, session, () => sessions[uid] = session);
        logger.LogInformation("Uid {Uid} is online", uid);
        await RaiseAsync(RoleOnline, uid);
    }

    internal Task ClaimAsync(ulong uid, GcpSession session) => ownership.ClaimAsync(uid, session);

    internal async Task ReleaseAsync(ulong uid, GcpSession session)
    {
        if (ownership.Release(uid, session)
            && sessions.TryGetValue(uid, out var abandoned)
            && !abandoned.IsOpen
            && sessions.TryRemove(new KeyValuePair<ulong, GcpSession>(uid, abandoned)))
        {
            closedNextSvrPkgSeqs[uid] = abandoned.NextSvrPkgSeq;
            await RaiseAsync(RoleOffline, uid);
        }
    }

    internal int? FindNextSvrPkgSeq(ulong uid) =>
        sessions.TryGetValue(uid, out var session) ? session.NextSvrPkgSeq
        : closedNextSvrPkgSeqs.TryGetValue(uid, out var closedNextSvrPkgSeq) ? closedNextSvrPkgSeq
        : null;

    internal async Task DetachAsync(ulong uid, GcpSession session)
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
