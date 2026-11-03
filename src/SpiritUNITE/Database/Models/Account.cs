using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("account")]
public sealed class Account
{
    [PrimaryKey]
    [Identity]
    [Column("account_id")]
    public ulong AccountId { get; set; }

    [Column("user_name")]
    public string? UserName { get; set; }

    [Column("email")]
    public string? Email { get; set; }

    [Column("password_hash")]
    public byte[]? PasswordHash { get; set; }

    [Column("password_salt")]
    public byte[]? PasswordSalt { get; set; }

    [Column("password_iterations")]
    public int? PasswordIterations { get; set; }

    [Column("guest_id_hash")]
    public byte[]? GuestIdHash { get; set; }

    [Column("last_login_at")]
    public DateTime? LastLoginAt { get; set; }
}
