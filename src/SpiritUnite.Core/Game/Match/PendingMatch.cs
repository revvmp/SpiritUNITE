using SpiritUnite.Databin;
using SpiritUnite.Game.Room;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Match;

public sealed record MatchTicket(int MatchID);

public sealed record MatchSearch(DateTimeOffset FillAt);

public sealed record MatchConfirmWindow(MatchTicket Ticket, int MapId, DateTimeOffset Deadline, IReadOnlyList<ulong> NpcUids);

public sealed class PendingMatch(
    int matchID,
    IReadOnlyList<MatchRoom> rooms,
    IReadOnlyList<MatchQueueEntry> queueEntries,
    BotMatchSucc matchSucc,
    bool npcsConfirm)
{
    public MatchTicket Ticket { get; } = new(matchID);
    public IReadOnlyList<MatchRoom> Rooms { get; } = rooms;
    public IReadOnlyList<MatchQueueEntry> QueueEntries { get; } = queueEntries;
    public pbSCMatchRsp MatchRsp { get; } = matchSucc.MatchRsp;
    public IReadOnlyDictionary<ulong, pbAiInfoConfig> NpcConfigs { get; } = matchSucc.NpcConfigs;
    public int WarmBattleType { get; } = matchSucc.WarmBattleType;
    public HashSet<ulong> ConfirmedUids { get; } = [];
    public HashSet<ulong> UnconfirmedNpcUids { get; } = npcsConfirm ? [.. matchSucc.NpcConfigs.Keys] : [];

    public pbCOMDT_MATCH_SUCC_DETAIL MatchSucc => MatchRsp.MatchResult.MatchResDetail.MatchSucc;
    public MatchRoom LeadRoom => Rooms[0];
    public int MapId => LeadRoom.MapId;

    public ulong[] HumanUids => [.. Rooms.SelectMany(room => room.Members).Select(member => member.Uid)];

    public ulong[] ConfirmingUids =>
        [.. Rooms.SelectMany(room => room.Members).Where(member => member.CampClient != MatchRoom.JudgeCamp).Select(member => member.Uid)];

    public bool IsConfirmed => UnconfirmedNpcUids.Count == 0 && ConfirmingUids.All(ConfirmedUids.Contains);

    public DateTimeOffset ConfirmDeadline =>
        DateTimeOffset.FromUnixTimeSeconds(MatchSucc.SuccTimeStamp).AddMilliseconds(MatchSucc.ConfirmTimeoutMs);

    public MatchConfirmWindow ConfirmWindow => new(Ticket, MapId, ConfirmDeadline, [.. UnconfirmedNpcUids]);

    public MatchRoom? RoomOf(ulong uid) => Rooms.FirstOrDefault(room => room.FindMember(uid) is not null);

    public MatchQueueEntry? QueueEntryOf(MatchRoom room) => QueueEntries.FirstOrDefault(entry => entry.Room == room);
}

public static class MatchConfirmTimeout
{
    public static int Of(GameData gameData, int mapSubMode) => gameData.GetGlobalIntData(
        mapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank
            ? PB_ENUM_RES_RESGLOBAL.PbResglobalConfirmTimeoutMsRank
            : PB_ENUM_RES_RESGLOBAL.PbResglobalConfirmTimeoutMsRelax);
}
