using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Credit;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Match;

public sealed class MatchScheduler(
    RoomManager rooms,
    LobbyRoles lobbyRoles,
    FriendPresence presence,
    CreditManager credit,
    TimeProvider clock,
    ILogger<MatchScheduler> logger) : BackgroundService
{
    private static readonly TimeSpan FormInterval = TimeSpan.FromSeconds(1);

    public event Func<PendingMatch, Task>? MatchConfirmed;

    public void Follow(RoomOutcome outcome)
    {
        foreach (var search in outcome.Searches)
        {
            _ = FormAtAsync(search.FillAt);
        }

        foreach (var window in outcome.ConfirmWindows)
        {
            logger.LogInformation(
                "Match {MatchID} on map {MapId}: humans confirm until {Deadline:O}, {NpcCount} NPCs confirm on their own",
                window.Ticket.MatchID,
                window.MapId,
                window.Deadline,
                window.NpcUids.Count);
            _ = ExpireConfirmAsync(window);
            foreach (var npcUid in window.NpcUids)
            {
                _ = ConfirmNpcAsync(window.Ticket, npcUid, CpuMatch.PickNpcConfirmDelay());
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(FormInterval, clock, stoppingToken);
                await FormMatchesAsync();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Matchmaking stopped");
        }
    }

    private async Task FormAtAsync(DateTimeOffset fillAt)
    {
        var remaining = fillAt - clock.GetUtcNow();
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, clock);
        }

        await FormMatchesAsync();
    }

    private async Task FormMatchesAsync()
    {
        try
        {
            var outcome = rooms.FormMatches();
            if (outcome.ConfirmWindows.Count == 0)
            {
                return;
            }

            await PushAllAsync(outcome);
            Follow(outcome);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Forming matches failed");
        }
    }

    private async Task ConfirmNpcAsync(MatchTicket ticket, ulong npcUid, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, clock);
            var outcome = rooms.ConfirmNpc(ticket, npcUid);
            await PushAllAsync(outcome);
            if (outcome.StartedGame is { } match)
            {
                logger.LogInformation("Match {MatchID}: the last NPC confirmed; the match starts", match.Ticket.MatchID);
                await RaiseMatchConfirmedAsync(match);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "NPC {NpcUid} confirm for match {MatchID} failed", npcUid, ticket.MatchID);
        }
    }

    private async Task ExpireConfirmAsync(MatchConfirmWindow window)
    {
        try
        {
            var remaining = window.Deadline - clock.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                await Task.Delay(remaining, clock);
            }

            var outcome = rooms.ExpireMatchConfirm(window.Ticket, out var removedUids);
            if (outcome.Pushes.Count == 0)
            {
                return;
            }

            logger.LogInformation(
                "Match {MatchID} aborted: uids {Uids} did not confirm within the confirm window; {RequeuedCount} confirmed parties search again",
                window.Ticket.MatchID,
                removedUids,
                outcome.Searches.Count);
            var penalizedUids = new HashSet<ulong>();
            foreach (var uid in removedUids)
            {
                if ((await credit.PenalizeUnconfirmedMatchAsync(uid, window.MapId)).IsPenalized)
                {
                    penalizedUids.Add(uid);
                }
            }

            await PushAllAsync(await WithCreditNoticesAsync(outcome.Pushes, penalizedUids));
            Follow(outcome);
            foreach (var uid in removedUids)
            {
                await presence.SetGameStateAsync(uid, PB_ENUM_E_ACNT_GAMESTATE.PbEacntGameidle, 0);
            }

            if (penalizedUids.Count > 0)
            {
                await Task.Delay(CreditManager.UnconfirmedMatchMailDelay, clock);
                foreach (var uid in penalizedUids)
                {
                    await credit.SendUnconfirmedMatchMailAsync(uid);
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Match confirm expiry for match {MatchID} failed", window.Ticket.MatchID);
        }
    }

    private async Task RaiseMatchConfirmedAsync(PendingMatch match)
    {
        if (MatchConfirmed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<PendingMatch, Task>>())
        {
            await handler(match);
        }
    }

    private async Task<List<RoomPush>> WithCreditNoticesAsync(IReadOnlyList<RoomPush> pushes, HashSet<ulong> penalizedUids)
    {
        var noticed = new HashSet<ulong>();
        var ordered = new List<RoomPush>(pushes.Count + penalizedUids.Count);
        foreach (var push in pushes)
        {
            ordered.Add(push);
            if (push.Message.MsgID == PB_ENUM_CS_MSGID_DEF.PbScidMultGameAbortNtf
                && penalizedUids.Contains(push.Uid)
                && noticed.Add(push.Uid))
            {
                ordered.Add(new RoomPush(push.Uid, new RoomMessage(
                    PB_ENUM_CS_MSGID_DEF.PbScidCreditPointNtf,
                    await credit.BuildCreditPointNtfAsync(push.Uid))));
            }
        }

        return ordered;
    }

    private Task PushAllAsync(RoomOutcome outcome) => PushAllAsync(outcome.Pushes);

    private async Task PushAllAsync(IEnumerable<RoomPush> pushes)
    {
        foreach (var group in pushes.GroupBy(push => push.Uid))
        {
            var packets = group.Select(push => GamePacket.Create(push.Message.MsgID, push.Message.Message)).ToArray();
            if (!await lobbyRoles.PushAsync(group.Key, packets))
            {
                logger.LogInformation("Match notices for uid {Uid} were not delivered: the role is offline", group.Key);
            }
        }
    }
}
