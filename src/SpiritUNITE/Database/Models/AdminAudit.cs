using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("admin_audit")]
public sealed class AdminAudit
{
    [PrimaryKey]
    [Identity]
    [Column("audit_id")]
    public ulong AuditId { get; set; }

    [Column("audit_time")]
    public long AuditTime { get; set; }

    [Column("token_id")]
    public uint TokenId { get; set; }

    [Column("token_name")]
    public string TokenName { get; set; } = string.Empty;

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("target")]
    public string Target { get; set; } = string.Empty;

    [Column("detail")]
    public string Detail { get; set; } = string.Empty;
}
