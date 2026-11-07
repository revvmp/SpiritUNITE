using SpiritUNITE.Game.Room;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Match;

public sealed record MatchKey(int MapSubMode, int MapId, int ManMachine, int RankedTeamMemberNumType)
{
    public static MatchKey Of(MatchRoom room) => new(
        room.MapSubMode,
        room.MapId,
        room.ManMachine,
        room.MapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank ? room.RankedTeamMemberNumType : 0);
}

public sealed record MasterPointBand(int MinMasterPoint, int MaxMasterPoint, int Width)
{
    public bool Admits(MasterPointBand other) =>
        Math.Max(MaxMasterPoint, other.MaxMasterPoint) - Math.Min(MinMasterPoint, other.MinMasterPoint) <= Width;

    public MasterPointBand With(MasterPointBand other) => this with
    {
        MinMasterPoint = Math.Min(MinMasterPoint, other.MinMasterPoint),
        MaxMasterPoint = Math.Max(MaxMasterPoint, other.MaxMasterPoint),
    };
}

public sealed record RankedBand(int MinBig, int MaxBig, int Width, MasterPointBand? MasterPoints = null)
{
    public bool Admits(RankedBand other) =>
        MasterPoints is { } masterPoints && other.MasterPoints is { } otherMasterPoints
            ? masterPoints.Admits(otherMasterPoints)
            : Math.Max(MaxBig, other.MaxBig) - Math.Min(MinBig, other.MinBig) <= Width;

    public RankedBand With(RankedBand other) => this with
    {
        MinBig = Math.Min(MinBig, other.MinBig),
        MaxBig = Math.Max(MaxBig, other.MaxBig),
        MasterPoints = MasterPoints is { } masterPoints && other.MasterPoints is { } otherMasterPoints
            ? masterPoints.With(otherMasterPoints)
            : null,
    };
}

public sealed class MatchQueueEntry(
    MatchRoom room,
    DateTimeOffset enqueuedAt,
    DateTimeOffset fillAt,
    int warmBattleType,
    RankedBand? rankedBand = null)
{
    public MatchRoom Room { get; } = room;
    public MatchKey Key { get; } = MatchKey.Of(room);
    public DateTimeOffset EnqueuedAt { get; } = enqueuedAt;
    public DateTimeOffset FillAt { get; } = fillAt;
    public int WarmBattleType { get; } = warmBattleType;
    public bool IsWarmBattle => WarmBattleType != MatchGroup.NoWarmBattle;
    public RankedBand? RankedBand { get; } = rankedBand;
    public int PartySize => Room.Members.Count;
    public bool IsCpuMatch => CpuMatch.IsCpuMatch(Room);
}

public sealed record MatchGroup(
    IReadOnlyList<MatchQueueEntry> LeftCamp,
    IReadOnlyList<MatchQueueEntry> RightCamp,
    bool IsWarmBattle,
    int WarmBattleType = 0)
{
    public const int NoWarmBattle = (int)PB_ENUM_ENM_WARMBATTLE_TYPE.PbEnmWarmbattleNone;
    public const int Rookie = (int)PB_ENUM_ENM_WARMBATTLE_TYPE.PbEnmWarmbattleRookie;
    public const int ContLose = (int)PB_ENUM_ENM_WARMBATTLE_TYPE.PbEnmWarmbattleContlose;
    public const int Timeout = (int)PB_ENUM_ENM_WARMBATTLE_TYPE.PbEnmWarmbattleTimeout;

    public IEnumerable<MatchQueueEntry> Entries => LeftCamp.Concat(RightCamp);
    public MatchRoom LeadRoom => LeftCamp[0].Room;
}

public sealed class Matchmaker
{
    private readonly List<MatchQueueEntry> queue = [];

    public void Enqueue(MatchQueueEntry entry)
    {
        var index = queue.FindIndex(queued => queued.EnqueuedAt > entry.EnqueuedAt);
        queue.Insert(index < 0 ? queue.Count : index, entry);
    }

    public void Remove(MatchRoom room) => queue.RemoveAll(entry => entry.Room == room);

    public List<MatchGroup> FormMatches(DateTimeOffset now, bool botFill)
    {
        var formed = new List<MatchGroup>();
        foreach (var entry in queue.Where(entry => entry.IsWarmBattle && entry.FillAt <= now).ToArray())
        {
            queue.Remove(entry);
            formed.Add(new MatchGroup([entry], [], IsWarmBattle: true, entry.WarmBattleType));
        }

        foreach (var key in queue.Select(entry => entry.Key).Distinct().ToArray())
        {
            while (TryForm(key, now, botFill) is { } group)
            {
                foreach (var entry in group.Entries)
                {
                    queue.Remove(entry);
                }

                formed.Add(group);
            }
        }

        return formed;
    }

    private MatchGroup? TryForm(MatchKey key, DateTimeOffset now, bool botFill)
    {
        var waiting = queue.Where(entry => entry.Key == key && !entry.IsWarmBattle).ToList();
        if (waiting.Count == 0)
        {
            return null;
        }

        var anchor = waiting[0];
        var campCapacity = anchor.Room.CampPlayerNum;
        var campCount = anchor.IsCpuMatch ? 1 : 2;
        var camps = Enumerable.Range(0, campCount).Select(_ => new List<MatchQueueEntry>()).ToArray();
        camps[0].Add(anchor);
        var candidates = waiting
            .Skip(1)
            .OrderByDescending(entry => entry.PartySize)
            .ThenBy(entry => entry.EnqueuedAt);
        var band = anchor.RankedBand;
        foreach (var candidate in candidates)
        {
            if (band is not null && candidate.RankedBand is { } candidateBand && !band.Admits(candidateBand))
            {
                continue;
            }

            var camp = camps
                .Where(camp => FreeSeats(camp, campCapacity) >= candidate.PartySize)
                .OrderByDescending(camp => FreeSeats(camp, campCapacity))
                .FirstOrDefault();
            if (camp is null)
            {
                continue;
            }

            camp.Add(candidate);
            band = band is not null && candidate.RankedBand is { } joined ? band.With(joined) : band;
        }

        var isFull = camps.All(camp => FreeSeats(camp, campCapacity) == 0);
        if (!isFull && !(botFill && anchor.FillAt <= now))
        {
            return null;
        }

        return !isFull && !anchor.IsCpuMatch && camps.Sum(camp => camp.Count) == 1 && WarmBattleManager.IsWarmBattleSubMode(anchor.Room.MapSubMode)
            ? new MatchGroup([anchor], [], IsWarmBattle: true, MatchGroup.Timeout)
            : new MatchGroup(camps[0], campCount > 1 ? camps[1] : [], IsWarmBattle: false);
    }

    private static int FreeSeats(List<MatchQueueEntry> camp, int campCapacity) =>
        campCapacity - camp.Sum(entry => entry.PartySize);
}
