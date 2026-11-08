using System.Collections.Frozen;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Match;

public sealed class MatchOptions
{
    public const string SectionName = "Match";

    private const int MinBotFillSearchSec = 1;
    private const int MaxBotFillSearchSec = 3600;
    private const int MinContLoseWarmBattleLosses = 1;

    public bool BotFill { get; init; } = true;
    public int BotFillSearchSec { get; init; } = 80;
    public int ContLoseWarmBattleLosses { get; init; } = 3;
    private const int WaitTimeBounds = 2;
    private const int RankedMapSubMode = (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank;

    private static readonly FrozenDictionary<int, int[]> OfficialWarmBattleWaitTime = new Dictionary<int, int[]>
    {
        [RankedMapSubMode] = [12, 14],
    }.ToFrozenDictionary();

    public Dictionary<int, int[]>? WarmBattleWaitTime { get; init; }

    public IReadOnlyDictionary<int, int[]> WarmBattleWaitTimeOrOfficial =>
        WarmBattleWaitTime ?? (IReadOnlyDictionary<int, int[]>)OfficialWarmBattleWaitTime;

    public bool IsValid() =>
        BotFillSearchSec is >= MinBotFillSearchSec and <= MaxBotFillSearchSec
        && ContLoseWarmBattleLosses >= MinContLoseWarmBattleLosses
        && WarmBattleWaitTimeOrOfficial.All(entry => entry.Key > 0 && entry.Value is [>= 0 and var min, var max] && min <= max);

    public int? DrawWarmBattleWaitTime(int mapSubMode) =>
        WarmBattleWaitTimeOrOfficial.TryGetValue(mapSubMode, out var bounds) && bounds.Length == WaitTimeBounds
            ? Random.Shared.Next(bounds[0], bounds[1] + 1)
            : null;
}
