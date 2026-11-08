using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_eevee_challenge")]
public sealed class RoleEeveeChallenge
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [PrimaryKey(2)]
    [Column("period")]
    public int Period { get; set; }

    [Column("eevee_id")]
    public int EeveeID { get; set; }

    [Column("is_click")]
    public int IsClick { get; set; }

    [Column("draw_time")]
    public long DrawTime { get; set; }
}
