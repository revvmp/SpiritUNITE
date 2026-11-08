using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("gift_code_use")]
public sealed class GiftCodeUse
{
    [PrimaryKey(0)]
    [Column("gift_code")]
    public string GiftCode { get; set; } = string.Empty;

    [PrimaryKey(1)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("use_time")]
    public long UseTime { get; set; }

    [Column("mail_index")]
    public int MailIndex { get; set; }
}
