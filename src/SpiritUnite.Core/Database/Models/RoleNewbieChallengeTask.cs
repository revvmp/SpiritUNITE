using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_newbie_challenge_task")]
public sealed class RoleNewbieChallengeTask
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("task_id")]
    public int TaskId { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
