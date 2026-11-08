using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_activity_task_finish")]
public sealed class RoleActivityTaskFinish
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

    [Column("finish_time")]
    public long FinishTime { get; set; }
}
