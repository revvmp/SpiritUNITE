using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_mega_evolution_reward")]
public sealed class RoleMegaEvolutionReward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [PrimaryKey(2)]
    [Column("award_key")]
    public int AwardKey { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
