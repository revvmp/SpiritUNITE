using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_mega_evolution")]
public sealed class RoleMegaEvolution
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [Column("complete")]
    public int Complete { get; set; }

    [Column("cur_stage")]
    public int CurStage { get; set; }

    [Column("last_daily_update_time")]
    public long LastDailyUpdateTime { get; set; }

    [Column("last_week_update_time")]
    public long LastWeekUpdateTime { get; set; }
}
