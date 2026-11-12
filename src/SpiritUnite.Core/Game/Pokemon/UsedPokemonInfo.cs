using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Pokemon;

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
    pbCOMDT_SKILLRECOMMENDINFO? SkillRecommendInfo = null,
    int UseCount = 0,
    long LastUseTime = 0)
{
    public const long NeverWon = 0;

    private const long BattleTimeEpoch = 1577808000;
    private const int SkillRecommendGameType = 0;

    public static UsedPokemonInfo Unused(int pokemonId) => new(pokemonId, []);

    public static pbCOMDT_RECENTLY_USED_POKEMON_SORT_LIST_INFO BuildRecentlyUsedPokemonList(IEnumerable<UsedPokemonInfo> usedPokemon) => new()
    {
        SortPokemonList =
        {
            usedPokemon
                .Where(used => used.UseCount > 0)
                .OrderByDescending(used => used.UseCount)
                .ThenByDescending(used => used.LastUseTime)
                .Select(used => new pbCOMDT_RECENTLY_USED_POKEMON_COUNT_INFO
                {
                    PokemonId = used.PokemonId,
                    UseCount = used.UseCount,
                    LastUseTimeStamp = used.LastUseTime,
                }),
        },
    };

    public pbCOMDT_HELDITEM_DESIGN_INFO BuildHeldItemDesignInfo() => new() { HeldItemID = { HeldItemID } };

    public UsedPokemonStatistics BattleStatistics => Statistics ?? UsedPokemonStatistics.None;

    public pbCOMDT_USED_POKEMON_INFO ToUsedPokemonInfo(pbCOMDT_CARRYEQUIPMENT_PLAN_INFO carryEquipmentPlanInfo) => new()
    {
        PokemonId = PokemonId,
        HeldItemDesignInfo = BuildHeldItemDesignInfo(),
        StatisticInfo = BattleStatistics.Total.Clone(),
        LastBattleTime = ToBattleTime(BattleStatistics.LastBattleTime),
        CurrentSeasonID = BattleStatistics.CurrentSeasonID,
        WearPasterID = WearPasterID,
        CarryEquipmentPlanInfo = { carryEquipmentPlanInfo.Clone() },
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
