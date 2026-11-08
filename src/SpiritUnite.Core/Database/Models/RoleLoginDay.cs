using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_login_day")]
public sealed class RoleLoginDay
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("login_day")]
    public DateOnly LoginDay { get; set; }
}
