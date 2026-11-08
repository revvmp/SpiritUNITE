using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_activation_code")]
public sealed class RoleActivationCode
{
    [PrimaryKey]
    [Column("activation_code")]
    public string ActivationCode { get; set; } = string.Empty;

    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("get_time")]
    public long GetTime { get; set; }
}
