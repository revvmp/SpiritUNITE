using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_battle_pass")]
public sealed class RoleBattlePass
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("season_id")]
    public int SeasonID { get; set; }

    [Column("level")]
    public int Level { get; set; }

    [Column("exp")]
    public int Exp { get; set; }

    [Column("total_exp")]
    public int TotalExp { get; set; }

    [Column("pass_type")]
    public int PassType { get; set; }

    [Column("video_played")]
    public int VideoPlayed { get; set; }

    [Column("addition_treasure_cnt")]
    public int AdditionTreasureCnt { get; set; }

    [Column("addition_treasure_unget_cnt")]
    public int AdditionTreasureUnGetCnt { get; set; }

    [Column("addition_treasure_unget_exp")]
    public int AdditionTreasureUnGetExp { get; set; }

    [Column("last_up_level_time")]
    public long LastUpLevelTime { get; set; }

    [Column("last_gain_exp_time")]
    public long LastGainExpTime { get; set; }

    [Column("season_settled")]
    public int SeasonSettled { get; set; }
}
