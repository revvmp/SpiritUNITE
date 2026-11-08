using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_mega_evolution_task")]
public sealed class RoleMegaEvolutionTask
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [PrimaryKey(2)]
    [Column("task_type")]
    public int TaskType { get; set; }

    [PrimaryKey(3)]
    [Column("task_id")]
    public int TaskID { get; set; }
}
