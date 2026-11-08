using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_unite_challenge_reward")]
public sealed class RoleUniteChallengeReward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("period_id")]
    public int PeriodId { get; set; }

    [PrimaryKey(2)]
    [Column("reward_index")]
    public int RewardIndex { get; set; }

    [Column("reward_status")]
    public int RewardStatus { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
