using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_paster_time_limited")]
public sealed class RolePasterTimeLimited
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("paster_id")]
    public int PasterId { get; set; }

    [Column("gain_time")]
    public long GainTime { get; set; }

    [Column("new_tag")]
    public int NewTag { get; set; }

    [Column("time_limited")]
    public long TimeLimited { get; set; }
}
