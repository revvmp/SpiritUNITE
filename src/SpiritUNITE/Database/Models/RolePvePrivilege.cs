using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_pve_privilege")]
public sealed class RolePvePrivilege
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [Column("privilege_expire_time")]
    public long PrivilegeExpireTime { get; set; }
}
