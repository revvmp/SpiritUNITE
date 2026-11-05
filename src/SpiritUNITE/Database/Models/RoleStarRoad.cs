using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_star_road")]
public sealed class RoleStarRoad
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("tips_site_id")]
    public int TipsSiteID { get; set; }
}
