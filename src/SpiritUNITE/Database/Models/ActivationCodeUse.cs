using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("activation_code_use")]
public sealed class ActivationCodeUse
{
    [PrimaryKey(0)]
    [Column("activation_code")]
    public string ActivationCode { get; set; } = string.Empty;

    [PrimaryKey(1)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("use_time")]
    public long UseTime { get; set; }
}
