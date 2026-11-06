using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Credit;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Room;

public sealed class MatchScheduler(
    RoomManager rooms,
    LobbyRoles lobbyRoles,
    FriendPresence presence,
    CreditManager credit,
    TimeProvider clock,
    ILogger<MatchScheduler> logger)
{
    public event Func<MatchRoom, Task>? MatchConfirmed;

    public void Follow(RoomOutcome outcome)
    {
        if (outcome.Search is { } search)
        {
            logger.LogInformation(
                "Room {RoomID} searches for a bot-filled match; bots fill it in {SearchSec} s",
                search.Ticket.RoomID,
                search.Delay.TotalSeconds);
            _ = CompleteSearchAsync(search);
        }

        if (outcome.ConfirmWindow is { } window)
        {
            logger.LogInformation(
                "Room {RoomID} matched against bots; members confirm until {Deadline:O}, {NpcCount} NPCs confirm on their own",
                window.Ticket.RoomID,
                window.Deadline,
                window.NpcUids.Count);
            _ = ExpireConfirmAsync(window);
            foreach (var npcUid in window.NpcUids)
            {
                _ = ConfirmNpcAsync(window.Ticket, npcUid, CpuMatch.PickNpcConfirmDelay());
            }
        }
    }

    private async Task CompleteSearchAsync(MatchSearch search)
    {
        try
        {
            await Task.Delay(search.Delay, clock);
            var outcome = rooms.CompleteSearch(search.Ticket);
            if (outcome.ConfirmWindow is null)
            {
                return;
            }

            await PushAllAsync(outcome);
            Follow(outcome);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Bot-filled match search for room {RoomID} failed", search.Ticket.RoomID);
        }
    }

    private async Task ConfirmNpcAsync(MatchTicket ticket, ulong npcUid, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, clock);
            var outcome = rooms.ConfirmNpc(ticket, npcUid);
            await PushAllAsync(outcome);
            if (outcome.StartedGame is { } room)
            {
                logger.LogInformation("Room {RoomID}: the last NPC confirmed; the match starts", room.RoomID);
                await RaiseMatchConfirmedAsync(room);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "NPC {NpcUid} confirm for room {RoomID} failed", npcUid, ticket.RoomID);
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
                "Room {RoomID} match aborted: uids {Uids} did not confirm within the confirm window",
                window.Ticket.RoomID,
                removedUids);
            var penalizedUids = new HashSet<ulong>();
            foreach (var uid in removedUids)
            {
                if ((await credit.PenalizeUnconfirmedMatchAsync(uid, window.MapId)).IsPenalized)
                {
                    penalizedUids.Add(uid);
                }
            }

            await PushAllAsync(await WithCreditNoticesAsync(outcome.Pushes, penalizedUids));
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
            logger.LogError(exception, "Match confirm expiry for room {RoomID} failed", window.Ticket.RoomID);
        }
    }

    private async Task RaiseMatchConfirmedAsync(MatchRoom room)
    {
        if (MatchConfirmed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<MatchRoom, Task>>())
        {
            await handler(room);
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
