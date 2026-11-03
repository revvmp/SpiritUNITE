using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("account_token")]
public sealed class AccountToken
{
    [PrimaryKey]
    [Column("token_hash")]
    public byte[] TokenHash { get; set; } = [];

    [Column("account_id")]
    public ulong AccountId { get; set; }

    [Column("expires_at")]
    public long ExpiresAt { get; set; }
}
