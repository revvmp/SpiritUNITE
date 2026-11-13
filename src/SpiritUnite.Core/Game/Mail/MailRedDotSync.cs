using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Mail;

public sealed class MailRedDotSync
{
    private const int MaxConcurrentPushes = 16;
    private const int MaxStaleCorrections = 3;

    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(1);
    private static readonly AsyncLocal<bool> SyncSending = new();

    private readonly MailRepository repository;
    private readonly LobbyRoles lobbyRoles;
    private readonly TimeProvider clock;
    private readonly ILogger<MailRedDotSync> logger;
    private readonly long syncIntervalTimestamps;
    private readonly Lock gate = new();
    private readonly Dictionary<ulong, long> periodStarts = [];
    private readonly Dictionary<ulong, long> scheduledSyncs = [];
    private readonly Dictionary<ulong, long> mailStamps = [];
    private readonly PriorityQueue<ulong, long> dueSyncs = new();

    public MailRedDotSync(MailRepository repository, LobbyRoles lobbyRoles, TimeProvider clock, ILogger<MailRedDotSync> logger)
    {
        this.repository = repository;
        this.lobbyRoles = lobbyRoles;
        this.clock = clock;
        this.logger = logger;
        syncIntervalTimestamps = (long)(SyncInterval.TotalSeconds * clock.TimestampFrequency);
        lobbyRoles.RoleOnline += OnRoleOnlineAsync;
        lobbyRoles.RoleOffline += OnRoleOfflineAsync;
        lobbyRoles.PacketsSending += OnPacketsSendingAsync;
    }

    public TimeSpan UntilNextSync()
    {
        lock (gate)
        {
            if (!dueSyncs.TryPeek(out _, out var syncTime))
            {
                return MaxWait;
            }

            var wait = clock.GetElapsedTime(clock.GetTimestamp(), syncTime);
            return wait <= TimeSpan.Zero ? TimeSpan.Zero : wait < MaxWait ? wait : MaxWait;
        }
    }

    public async Task PushDueAsync(CancellationToken cancellationToken)
    {
        var dueRoles = TakeDue(clock.GetTimestamp());
        if (dueRoles.Count == 0)
        {
            return;
        }

        Dictionary<ulong, int[]> unread;
        try
        {
            unread = await repository.CountUnreadAsync([.. dueRoles.Select(due => due.Uid)], clock.GetUtcNow().ToUnixTimeSeconds());
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Mail red dot sync could not count unread mail for {Count} roles", dueRoles.Count);
            return;
        }

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentPushes, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(dueRoles, parallelOptions, async (due, _) =>
        {
            try
            {
                await SyncAsync(due.Uid, due.MailStamp, unread[due.Uid]);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Mail red dot sync push failed for uid {Uid}", due.Uid);
            }
        });
        logger.LogDebug("Mail red dot sync ran for {Count} roles", dueRoles.Count);
    }

    private async Task SyncAsync(ulong uid, long mailStamp, int[] unread)
    {
        if (!IsMailStampCurrent(uid, mailStamp))
        {
            return;
        }

        await PushSyncAsync(uid, MailManager.BuildUnreadRedDotPackets(unread));
        for (var correction = 0; correction < MaxStaleCorrections && !IsMailStampCurrent(uid, mailStamp); correction++)
        {
            mailStamp = MailStampOf(uid);
            var current = await repository.CountUnreadAsync(uid, clock.GetUtcNow().ToUnixTimeSeconds());
            await PushSyncAsync(uid, MailManager.BuildUnreadRedDotStatePackets(current));
        }
    }

    private async Task PushSyncAsync(ulong uid, GamePacket[] packets)
    {
        SyncSending.Value = true;
        try
        {
            await lobbyRoles.PushAsync(uid, packets);
        }
        finally
        {
            SyncSending.Value = false;
        }
    }

    private Task OnRoleOnlineAsync(ulong uid)
    {
        lock (gate)
        {
            var now = clock.GetTimestamp();
            if (!periodStarts.TryGetValue(uid, out var periodStart))
            {
                periodStart = now;
                periodStarts[uid] = periodStart;
            }

            Enqueue(uid, NextSyncTime(periodStart, now));
        }

        return Task.CompletedTask;
    }

    private Task OnRoleOfflineAsync(ulong uid)
    {
        lock (gate)
        {
            if (!lobbyRoles.IsOnline(uid))
            {
                scheduledSyncs.Remove(uid);
                mailStamps.Remove(uid);
            }
        }

        return Task.CompletedTask;
    }

    private Task OnPacketsSendingAsync(ulong uid, IReadOnlyList<GamePacket> packets)
    {
        if (SyncSending.Value || !packets.Any(IsMailRedDotChange))
        {
            return Task.CompletedTask;
        }

        lock (gate)
        {
            mailStamps[uid] = mailStamps.GetValueOrDefault(uid) + 1;
            if (packets.Any(packet => packet.MsgID == (int)PB_ENUM_CS_MSGID_DEF.PbScidMailNewNtf))
            {
                RestartPeriod(uid);
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsMailRedDotChange(GamePacket packet) => (PB_ENUM_CS_MSGID_DEF)packet.MsgID switch
    {
        PB_ENUM_CS_MSGID_DEF.PbScidMailNewNtf => true,
        PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf =>
            pbSCRedDotReplaceNtf.Parser.ParseFrom(packet.PkgData).RedDotInfo?.RedDotDetail.Any(detail => MailBox.IsUnreadRedDotID(detail.RedDotID)) == true,
        PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf => MailBox.IsUnreadRedDotID(pbSCRedDotClearNtf.Parser.ParseFrom(packet.PkgData).RedDotID),
        _ => false,
    };

    private void RestartPeriod(ulong uid)
    {
        if (!periodStarts.ContainsKey(uid))
        {
            return;
        }

        var now = clock.GetTimestamp();
        periodStarts[uid] = now;
        if (scheduledSyncs.ContainsKey(uid))
        {
            Enqueue(uid, now + syncIntervalTimestamps);
        }
    }

    private bool IsMailStampCurrent(ulong uid, long mailStamp) => MailStampOf(uid) == mailStamp;

    private long MailStampOf(ulong uid)
    {
        lock (gate)
        {
            return mailStamps.GetValueOrDefault(uid);
        }
    }

    private List<(ulong Uid, long MailStamp)> TakeDue(long now)
    {
        var due = new List<(ulong Uid, long MailStamp)>();
        lock (gate)
        {
            while (dueSyncs.TryPeek(out var uid, out var syncTime) && syncTime <= now)
            {
                dueSyncs.Dequeue();
                if (!scheduledSyncs.TryGetValue(uid, out var scheduled) || scheduled != syncTime)
                {
                    continue;
                }

                Enqueue(uid, NextSyncTime(periodStarts[uid], now));
                due.Add((uid, mailStamps.GetValueOrDefault(uid)));
            }
        }

        return due;
    }

    private void Enqueue(ulong uid, long syncTime)
    {
        scheduledSyncs[uid] = syncTime;
        dueSyncs.Enqueue(uid, syncTime);
    }

    private long NextSyncTime(long periodStart, long now) =>
        periodStart + syncIntervalTimestamps * ((now - periodStart) / syncIntervalTimestamps + 1);
}
