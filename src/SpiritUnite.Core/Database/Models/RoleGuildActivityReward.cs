using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_guild_activity_reward")]
public sealed class RoleGuildActivityReward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("period_id")]
    public int PeriodID { get; set; }

    [PrimaryKey(2)]
    [Column("reward_gear")]
    public int RewardGear { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
