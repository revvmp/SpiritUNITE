using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Pokemon;

public sealed record UsedPokemonInfo(
    int PokemonId,
    IReadOnlyList<int> HeldItemID,
    int LastCarryTrainerItemID = 0,
    int LastSchedueId = 0,
    int BattleNotUseBadge = 0,
    int WearPasterID = 0,
    int PokemonTotalPoint = 0,
    int PokemonSeasonPoint = 0,
    long LastBattleWinTime = 0,
    UsedPokemonStatistics? Statistics = null,
    pbCOMDT_SKILLRECOMMENDINFO? SkillRecommendInfo = null)
{
    public const long NeverWon = 0;

    private const long BattleTimeEpoch = 1577808000;
    private const int SkillRecommendGameType = 0;

    public static UsedPokemonInfo Unused(int pokemonId) => new(pokemonId, []);

    public pbCOMDT_HELDITEM_DESIGN_INFO BuildHeldItemDesignInfo() => new() { HeldItemID = { HeldItemID } };

    public UsedPokemonStatistics BattleStatistics => Statistics ?? UsedPokemonStatistics.None;

    public pbCOMDT_USED_POKEMON_INFO ToUsedPokemonInfo(IEnumerable<int> recommendEquips) => new()
    {
        PokemonId = PokemonId,
        HeldItemDesignInfo = BuildHeldItemDesignInfo(),
        StatisticInfo = BattleStatistics.Total.Clone(),
        LastBattleTime = ToBattleTime(BattleStatistics.LastBattleTime),
        CurrentSeasonID = BattleStatistics.CurrentSeasonID,
        WearPasterID = WearPasterID,
        CarryEquipmentPlanInfo = { new pbCOMDT_CARRYEQUIPMENT_PLAN_INFO { EquipmentID = { recommendEquips } } },
        SpecialEffectInfo = new pbCOMDT_POKEMON_SPECIALEFFECT_INFO(),
        LastCarryTrainerItemID = LastCarryTrainerItemID,
        LastHeldItemDesignInfo = new pbCOMDT_HELDITEM_DESIGN_INFO(),
        CurrentSeasonStatisticInfo = BattleStatistics.CurrentSeason.Clone(),
        RankedMatchInfoInSeason = BattleStatistics.RankedMatchInfoInSeason.Clone(),
        PokemonTotalPoint = PokemonTotalPoint,
        PokemonSeasonPoint = PokemonSeasonPoint,
        LastBattleWinTime = ToBattleTime(LastBattleWinTime),
        LastSchedueId = LastSchedueId,
        BattleNotUseBadge = BattleNotUseBadge,
        CurrentWeekStatisticInfo = BattleStatistics.CurrentWeek.Clone(),
        SkillWayInfo = new pbCOMDT_SKILL_WAY_INFO { UnlockInfo = new pbCOMDT_SKILL_UNLOCK_WAY_LIST() },
        SkillRecommendSetInfo =
        {
            SkillRecommendInfo is { } skillRecommendInfo
                ? [new pbCOMDT_SKILL_RECOMMEND_SET { GameType = SkillRecommendGameType, SkillRecommendInfo = skillRecommendInfo.Clone() }]
                : [],
        },
    };

    private static int ToBattleTime(long unixTime) => unixTime == NeverWon ? 0 : (int)(unixTime - BattleTimeEpoch);
}
