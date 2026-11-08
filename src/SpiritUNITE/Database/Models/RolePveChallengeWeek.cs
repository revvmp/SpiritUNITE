using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_pve_challenge_week")]
public sealed class RolePveChallengeWeek
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("week_start_time")]
    public long WeekStartTime { get; set; }

    [Column("week_reward_get_num")]
    public int WeekRewardGetNum { get; set; }

    [Column("privilege_reward_get_num")]
    public int PrivilegeRewardGetNum { get; set; }
}
