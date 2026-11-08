using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_battle_pass_task_reward")]
public sealed class RoleBattlePassTaskReward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("task_id")]
    public int TaskId { get; set; }

    [PrimaryKey(2)]
    [Column("period_start")]
    public long PeriodStart { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
