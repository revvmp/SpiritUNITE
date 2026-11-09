using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public sealed record MedalThresholds(int Bronze, int Silver, int Gold);

public sealed record PerformanceMedalTable(
    MedalThresholds Score,
    MedalThresholds Kill,
    MedalThresholds Assist,
    MedalThresholds DamageTank,
    MedalThresholds Cure);

public static class BattleSettlementMedals
{
    public const int QuickMatchMinutes = 5;
    public const int SilverStyleMvpScoreTTH = 600000;
    public const int GoldStyleMvpScoreTTH = 800000;

    public static readonly PerformanceMedalTable StandardMatchMedals = new(
        new MedalThresholds(60, 128, 180),
        new MedalThresholds(5, 8, 10),
        new MedalThresholds(5, 8, 10),
        new MedalThresholds(43000, 55000, 75000),
        new MedalThresholds(30000, 40000, 50000));

    public static readonly PerformanceMedalTable QuickMatchMedals = new(
        new MedalThresholds(60, 128, 180),
        new MedalThresholds(5, 8, 10),
        new MedalThresholds(2, 4, 5),
        new MedalThresholds(21500, 27500, 37500),
        new MedalThresholds(15000, 20000, 25000));

    private const int GoldToSilverStep = 1;
    private const int StyleMedalsPerOccupation = 2;
    private const int FirstStyleOccupation = (int)PB_ENUM_RES_POKEMON_OCCUPATION_TYPE.PbResPokemonOccupationTypeSupport;
    private const int LastStyleOccupation = (int)PB_ENUM_RES_POKEMON_OCCUPATION_TYPE.PbResPokemonOccupationTypeAgility;
    private const PB_ENUM_ENM_BATTLE_MASK_TYPE FirstGoldStyleMedal = PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeGlodenSupport;

    public static PerformanceMedalTable TableFor(int matchMinutes) =>
        matchMinutes == QuickMatchMinutes ? QuickMatchMedals : StandardMatchMedals;

    public static long BattleFlagOf(MvpPlayer player, int mvpScoreTTH, PerformanceMedalTable table) =>
        StyleFlagOf(player.OccupationType, mvpScoreTTH)
        | TierFlagOf(player.Stats.Score, table.Score,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxscoreBronze,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxscoreSilver,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxscoreGold)
        | TierFlagOf(player.Stats.KillCnt, table.Kill,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxkillBronze,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxkillSilver,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxkillGold)
        | TierFlagOf(player.Stats.AssistCnt, table.Assist,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxassistBronze,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxassistSilver,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxassistGold)
        | TierFlagOf(player.Stats.DamageTank, table.DamageTank,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxdamagetrankBronze,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxdamagetrankSilver,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxdamagetrankGold)
        | TierFlagOf(player.Stats.Cure, table.Cure,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxcureBronze,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxcureSilver,
            PB_ENUM_ENM_BATTLE_MASK_TYPE.PbEnmBattleMaskTypeMaxcureGold);

    public static long FlagOf(PB_ENUM_ENM_BATTLE_MASK_TYPE medal) => 1L << ((int)medal - 1);

    private static long StyleFlagOf(int occupationType, int mvpScoreTTH)
    {
        if (occupationType is < FirstStyleOccupation or > LastStyleOccupation || mvpScoreTTH < SilverStyleMvpScoreTTH)
        {
            return 0;
        }

        var goldMedal = FirstGoldStyleMedal + (occupationType - FirstStyleOccupation) * StyleMedalsPerOccupation;
        return FlagOf(mvpScoreTTH >= GoldStyleMvpScoreTTH ? goldMedal : goldMedal + GoldToSilverStep);
    }

    private static long TierFlagOf(
        int value,
        MedalThresholds thresholds,
        PB_ENUM_ENM_BATTLE_MASK_TYPE bronze,
        PB_ENUM_ENM_BATTLE_MASK_TYPE silver,
        PB_ENUM_ENM_BATTLE_MASK_TYPE gold) =>
        value >= thresholds.Gold ? FlagOf(gold)
        : value >= thresholds.Silver ? FlagOf(silver)
        : value >= thresholds.Bronze ? FlagOf(bronze)
        : 0;
}
