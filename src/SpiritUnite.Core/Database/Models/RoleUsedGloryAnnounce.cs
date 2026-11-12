using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_used_glory_announce")]
public sealed class RoleUsedGloryAnnounce
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("used_res_id")]
    public int UsedResID { get; set; }

    [Column("used_res_type")]
    public int UsedResType { get; set; }

    [Column("announce_id")]
    public int AnnounceID { get; set; }
}
