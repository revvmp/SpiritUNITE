using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_rename")]
public sealed class RoleRename
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("last_rename_time")]
    public long LastRenameTime { get; set; }
}
