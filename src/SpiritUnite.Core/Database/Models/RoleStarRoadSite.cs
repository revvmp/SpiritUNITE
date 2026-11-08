using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_star_road_site")]
public sealed class RoleStarRoadSite
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("site_id")]
    public int SiteID { get; set; }

    [Column("cur_pokemon_id")]
    public int CurPokemonID { get; set; }

    [Column("end_time")]
    public long EndTime { get; set; }
}
