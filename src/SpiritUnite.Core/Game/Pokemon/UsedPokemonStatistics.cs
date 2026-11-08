using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Pokemon;

public sealed record UsedPokemonBattle(bool Won, bool IsMvp, int MapSubMode, int SeasonId, long WeekStart, long BattleTime);

public sealed record UsedPokemonStatistics(
    pbCOMDT_USED_POKEMON_STATISTIC_INFO Total,
    int CurrentSeasonID,
    pbCOMDT_USED_POKEMON_STATISTIC_INFO CurrentSeason,
    long CurrentWeekStart,
    pbCOMDT_USED_POKEMON_STATISTIC_INFO CurrentWeek,
    long LastBattleTime,
    pbCOMDT_RANKED_MATCH_USED_POKEMON_INFO RankedMatchInfoInSeason)
{
    public static readonly UsedPokemonStatistics None = new(new(), 0, new(), 0, new(), 0, new());

    public UsedPokemonStatistics Record(UsedPokemonBattle battle) => new(
        Count(Total, battle),
        battle.SeasonId,
        Count(battle.SeasonId == CurrentSeasonID ? CurrentSeason : new(), battle),
        battle.WeekStart,
        Count(battle.WeekStart == CurrentWeekStart ? CurrentWeek : new(), battle),
        battle.BattleTime,
        CountRanked(battle.SeasonId == CurrentSeasonID ? RankedMatchInfoInSeason : new(), battle));

    private static pbCOMDT_RANKED_MATCH_USED_POKEMON_INFO CountRanked(pbCOMDT_RANKED_MATCH_USED_POKEMON_INFO rankedMatchInfo, UsedPokemonBattle battle)
    {
        var counted = rankedMatchInfo.Clone();
        if (battle.MapSubMode != (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank)
        {
            return counted;
        }

        counted.RankedMatchBattleCnt = Increment(counted.RankedMatchBattleCnt);
        if (battle.Won)
        {
            counted.RankedMatchWinCnt = Increment(counted.RankedMatchWinCnt);
        }

        return counted;
    }

    private static pbCOMDT_USED_POKEMON_STATISTIC_INFO Count(pbCOMDT_USED_POKEMON_STATISTIC_INFO statistic, UsedPokemonBattle battle)
    {
        var counted = statistic.Clone();
        counted.TotalBattleCnt = Increment(counted.TotalBattleCnt);
        if (battle.Won)
        {
            counted.TotalWinCnt = Increment(counted.TotalWinCnt);
        }

        if (battle.IsMvp)
        {
            counted.TotalMVPCnt = Increment(counted.TotalMVPCnt);
        }

        switch ((PB_ENUM_RES_MATCHMAPSUBMODE)battle.MapSubMode)
        {
            case PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceNormal:
                counted.TotalRaceNormalCnt = Increment(counted.TotalRaceNormalCnt);
                break;
            case PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank:
                counted.TotalRaceRankCnt = Increment(counted.TotalRaceRankCnt);
                break;
            case PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRelaxNormal:
                counted.TotalRaceRelaCnt = Increment(counted.TotalRaceRelaCnt);
                break;
        }

        return counted;
    }

    private static int Increment(int count) => count < int.MaxValue ? count + 1 : count;
}
