using System.Collections.Frozen;

namespace SpiritUNITE.Game.Ranked;

public sealed record FakePlayerWinPointRange(int Min, int Max);

public sealed class RankedOptions
{
    public const string SectionName = "Ranked";

    private static readonly int[] OfficialWinPoint = [20, 20, 20, 20, 30, 80, 80];
    private static readonly int[] OfficialLoseBasePoint = [0, 4, 0, 2, 0, 0, 0];
    private static readonly FrozenDictionary<int, int> OfficialSeasonInheritRankedDivision = new Dictionary<int, int>
    {
        [34] = 23,
        [33] = 23,
        [32] = 23,
        [31] = 23,
        [30] = 23,
        [26] = 22,
        [25] = 21,
        [24] = 20,
        [23] = 19,
        [22] = 16,
        [21] = 16,
        [20] = 15,
        [19] = 14,
        [18] = 14,
        [17] = 13,
        [16] = 13,
        [15] = 13,
        [14] = 12,
        [13] = 12,
        [12] = 11,
    }.ToFrozenDictionary();

    public int[]? WinPoint { get; init; }
    public int[]? LoseBasePoint { get; init; }
    public Dictionary<int, int>? SeasonInheritRankedDivision { get; init; }
    public int TeamMMRScore { get; init; } = 96;
    public int FakePlayerWinPointMin { get; init; } = 0;
    public int FakePlayerWinPointMax { get; init; } = 93;

    public IReadOnlyList<int> WinPointOrOfficial => WinPoint ?? OfficialWinPoint;

    public IReadOnlyList<int> LoseBasePointOrOfficial => LoseBasePoint ?? OfficialLoseBasePoint;

    public IReadOnlyDictionary<int, int> SeasonInheritRankedDivisionOrOfficial =>
        SeasonInheritRankedDivision ?? (IReadOnlyDictionary<int, int>)OfficialSeasonInheritRankedDivision;

    public int WinPointOf(int bigRankedDivision) => PointOf(WinPointOrOfficial, bigRankedDivision);

    public int LoseBasePointOf(int bigRankedDivision) => PointOf(LoseBasePointOrOfficial, bigRankedDivision);

    public bool FitsLadder(RankedLadder ladder) =>
        WinPointOrOfficial.Count == ladder.MaxBig
        && SeasonInheritRankedDivisionOrOfficial.All(pair => ladder.IsKnown(pair.Key) && ladder.IsKnown(pair.Value));

    public bool IsValid() =>
        WinPointOrOfficial.Count > 0
        && LoseBasePointOrOfficial.Count == WinPointOrOfficial.Count
        && WinPointOrOfficial.All(point => point >= 0)
        && LoseBasePointOrOfficial.All(point => point >= 0)
        && TeamMMRScore >= 0
        && FakePlayerWinPointMin >= 0
        && FakePlayerWinPointMax >= FakePlayerWinPointMin;

    private static int PointOf(IReadOnlyList<int> points, int bigRankedDivision) =>
        points.Count == 0 ? 0 : points[Math.Clamp(bigRankedDivision, 1, points.Count) - 1];
}
