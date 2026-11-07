using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_pokemon_used")]
public sealed class RolePokemonUsed
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("pokemon_id")]
    public int PokemonId { get; set; }

    [Column("held_item_design_info")]
    public byte[] HeldItemDesignInfo { get; set; } = [];

    [Column("last_carry_trainer_item_id")]
    public int LastCarryTrainerItemID { get; set; }

    [Column("last_schedue_id")]
    public int LastSchedueId { get; set; }

    [Column("battle_not_use_badge")]
    public int BattleNotUseBadge { get; set; }

    [Column("wear_paster_id")]
    public int WearPasterID { get; set; }

    [Column("pokemon_total_point")]
    public int PokemonTotalPoint { get; set; }

    [Column("pokemon_season_point")]
    public int PokemonSeasonPoint { get; set; }

    [Column("last_battle_win_time")]
    public long LastBattleWinTime { get; set; }

    [Column("statistic_info")]
    public byte[] StatisticInfo { get; set; } = [];

    [Column("current_season_id")]
    public int CurrentSeasonID { get; set; }

    [Column("current_season_statistic_info")]
    public byte[] CurrentSeasonStatisticInfo { get; set; } = [];

    [Column("current_week_start")]
    public long CurrentWeekStart { get; set; }

    [Column("current_week_statistic_info")]
    public byte[] CurrentWeekStatisticInfo { get; set; } = [];

    [Column("last_battle_time")]
    public long LastBattleTime { get; set; }

    [Column("ranked_match_info_in_season")]
    public byte[] RankedMatchInfoInSeason { get; set; } = [];

    [Column("skill_recommend_info")]
    public byte[] SkillRecommendInfo { get; set; } = [];
}
