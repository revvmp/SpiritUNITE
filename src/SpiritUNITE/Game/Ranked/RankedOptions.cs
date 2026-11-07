namespace SpiritUNITE.Game.Ranked;

public sealed record FakePlayerWinPointRange(int Min, int Max);

public sealed class RankedOptions
{
    public const string SectionName = "Ranked";

    public int[] WinPoint { get; init; } = [];
    public int[] LoseBasePoint { get; init; } = [];
    public Dictionary<int, int> SeasonInheritRankedDivision { get; init; } = [];
    public int TeamMMRScore { get; init; } = 96;
    public int FakePlayerWinPointMin { get; init; } = 0;
    public int FakePlayerWinPointMax { get; init; } = 93;

    public int WinPointOf(int bigRankedDivision) => PointOf(WinPoint, bigRankedDivision);

    public int LoseBasePointOf(int bigRankedDivision) => PointOf(LoseBasePoint, bigRankedDivision);

    public bool IsValid() =>
        WinPoint.Length > 0
        && LoseBasePoint.Length == WinPoint.Length
        && WinPoint.All(point => point >= 0)
        && LoseBasePoint.All(point => point >= 0)
        && TeamMMRScore >= 0
        && FakePlayerWinPointMin >= 0
        && FakePlayerWinPointMax >= FakePlayerWinPointMin;

    private static int PointOf(int[] points, int bigRankedDivision) =>
        points.Length == 0 ? 0 : points[Math.Clamp(bigRankedDivision, 1, points.Length) - 1];
}
