using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_task_process_value")]
public sealed class RoleTaskProcessValue
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

    [PrimaryKey(4)]
    [Column("counted_value")]
    public int CountedValue { get; set; }
}
