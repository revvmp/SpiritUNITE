using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_battle_pass_level_reward")]
public sealed class RoleBattlePassLevelReward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("season_id")]
    public int SeasonID { get; set; }

    [PrimaryKey(2)]
    [Column("level")]
    public int Level { get; set; }

    [PrimaryKey(3)]
    [Column("reward_type")]
    public int RewardType { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
