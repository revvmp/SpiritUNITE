using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_paster_spray")]
public sealed class RolePasterSpray
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("paster_id")]
    public int PasterId { get; set; }

    [PrimaryKey(2)]
    [Column("paster_spray_id")]
    public int PasterSprayID { get; set; }

    [Column("time_limited")]
    public long TimeLimited { get; set; }

    [Column("is_wearing")]
    public bool IsWearing { get; set; }
}
