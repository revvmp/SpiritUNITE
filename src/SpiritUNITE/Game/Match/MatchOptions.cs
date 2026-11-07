namespace SpiritUNITE.Game.Match;

public sealed class MatchOptions
{
    public const string SectionName = "Match";

    private const int MinBotFillSearchSec = 1;
    private const int MaxBotFillSearchSec = 3600;

    public bool BotFill { get; init; } = true;
    public int BotFillSearchSec { get; init; } = 60;
    private const int WaitTimeBounds = 2;

    public Dictionary<int, int[]> WarmBattleWaitTime { get; init; } = [];

    public bool IsValid() =>
        BotFillSearchSec is >= MinBotFillSearchSec and <= MaxBotFillSearchSec
        && WarmBattleWaitTime.All(entry => entry.Key > 0 && entry.Value is [>= 0 and var min, var max] && min <= max);

    public int? DrawWarmBattleWaitTime(int mapSubMode) =>
        WarmBattleWaitTime.TryGetValue(mapSubMode, out var bounds) && bounds.Length == WaitTimeBounds
            ? Random.Shared.Next(bounds[0], bounds[1] + 1)
            : null;
}
