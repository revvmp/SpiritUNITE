using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_task_process")]
public sealed class RoleTaskProcess
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

    [PrimaryKey(3)]
    [Column("condition_index")]
    public int ConditionIndex { get; set; }

    [Column("task_process")]
    public int TaskProcess { get; set; }

    [Column("complete_time")]
    public long CompleteTime { get; set; }
}
