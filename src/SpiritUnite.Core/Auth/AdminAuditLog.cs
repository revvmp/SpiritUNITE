using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;

namespace SpiritUnite.Auth;

public sealed record AdminAuditEntry(ulong AuditId, long AuditTime, string TokenName, string Action, string Target, string Detail);

public sealed class AdminAuditLog(AdminRepository repository, TimeProvider clock, ILogger<AdminAuditLog> logger)
{
    public const int PageSize = 50;
    private const int MaximumTargetLength = 80;

    public async Task RecordAsync(AdminIdentity admin, string action, string target, string detail)
    {
        logger.LogInformation("Admin {TokenName} ({TokenId}) {Action} {Target}: {Detail}", admin.TokenName, admin.TokenId, action, target, detail);
        await repository.AddAuditAsync(new AdminAudit
        {
            AuditTime = clock.GetUtcNow().ToUnixTimeSeconds(),
            TokenId = admin.TokenId,
            TokenName = admin.TokenName,
            Action = action,
            Target = target.Length > MaximumTargetLength ? target[..MaximumTargetLength] : target,
            Detail = detail,
        });
    }

    public async Task<AdminAuditEntry[]> ListAsync(ulong? beforeAuditId) =>
    [
        .. (await repository.ListAuditAsync(beforeAuditId, PageSize))
            .Select(audit => new AdminAuditEntry(audit.AuditId, audit.AuditTime, audit.TokenName, audit.Action, audit.Target, audit.Detail)),
    ];
}
