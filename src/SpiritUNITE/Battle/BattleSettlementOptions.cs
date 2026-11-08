namespace SpiritUNITE.Battle;

public sealed class BattleSettlementOptions
{
    public const string SectionName = "BattleSettlement";

    public int PveChallengeWinBaseExp { get; init; } = 150;
    public int PveChallengeLoseBaseExp { get; init; }
    public int MatchWinExpPerMinute { get; init; } = 15;
    public int MatchLoseExpPerMinute { get; init; } = 10;
    public int MatchWinEnergy { get; init; } = 15;
    public int MatchLoseEnergyPerMinute { get; init; } = 2;
    public int RankedMatchWinEnergy { get; init; } = 30;
    public int RankedMatchLoseEnergy { get; init; } = 20;

    public bool IsValid() => PveChallengeWinBaseExp >= 0
        && PveChallengeLoseBaseExp >= 0
        && MatchWinExpPerMinute >= 0
        && MatchLoseExpPerMinute >= 0
        && MatchWinEnergy >= 0
        && MatchLoseEnergyPerMinute >= 0
        && RankedMatchWinEnergy >= 0
        && RankedMatchLoseEnergy >= 0;
}
