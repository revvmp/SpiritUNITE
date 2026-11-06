namespace SpiritUNITE.Battle;

public sealed class BattleSettlementOptions
{
    public const string SectionName = "BattleSettlement";

    public int PveChallengeWinBaseExp { get; init; } = 150;
    public int PveChallengeLoseBaseExp { get; init; }

    public bool IsValid() => PveChallengeWinBaseExp >= 0 && PveChallengeLoseBaseExp >= 0;
}
