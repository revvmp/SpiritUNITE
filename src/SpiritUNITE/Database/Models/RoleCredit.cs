using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_credit")]
public sealed class RoleCredit
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("credit_point")]
    public int CreditPoint { get; set; }

    [Column("unconfirmed_day")]
    public long UnconfirmedDay { get; set; }

    [Column("unconfirmed_count")]
    public int UnconfirmedCount { get; set; }

    [Column("ban_end_time")]
    public long BanEndTime { get; set; }
}
