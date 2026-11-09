using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_credit_task")]
public sealed class RoleCreditTask
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("task_id")]
    public int TaskId { get; set; }

    [Column("task_status")]
    public int Status { get; set; }

    [Column("task_process")]
    public int TaskProcess { get; set; }

    [Column("complete_num")]
    public int CompleteNum { get; set; }

    [Column("recv_time")]
    public long RecvTime { get; set; }

    [Column("complete_time")]
    public long CompleteTime { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
