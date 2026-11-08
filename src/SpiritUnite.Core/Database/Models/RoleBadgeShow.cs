using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_badge_show")]
public sealed class RoleBadgeShow
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("badge_show_list")]
    public string BadgeShowList { get; set; } = string.Empty;
}
