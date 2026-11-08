using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("admin_token")]
public sealed class AdminToken
{
    [PrimaryKey]
    [Identity]
    [Column("token_id")]
    public uint TokenId { get; set; }

    [Column("token_name")]
    public string TokenName { get; set; } = string.Empty;

    [Column("token_hash")]
    public byte[] TokenHash { get; set; } = [];

    [Column("created_time")]
    public long CreatedTime { get; set; }

    [Column("last_used_time")]
    public long LastUsedTime { get; set; }
}
