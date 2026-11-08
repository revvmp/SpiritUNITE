using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_achievement")]
public sealed class RoleAchievement
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("achievement_id")]
    public int AchievementID { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
