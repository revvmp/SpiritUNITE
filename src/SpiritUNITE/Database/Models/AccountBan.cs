using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("account_ban")]
public sealed class AccountBan
{
    [PrimaryKey]
    [Column("account_id")]
    public ulong AccountId { get; set; }

    [Column("ban_end_time")]
    public long BanEndTime { get; set; }

    [Column("reason")]
    public string Reason { get; set; } = string.Empty;
}
