using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_badge")]
public sealed class RoleBadge
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("cur_badge_schedue_id")]
    public int CurBadgeSchedueId { get; set; }
}
