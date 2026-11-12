using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_achievement_score_reward")]
public sealed class RoleAchievementScoreReward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("total_score")]
    public int TotalScore { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
