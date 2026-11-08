using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed class AdminRepository(DataOptions options)
{
    public async Task<AdminToken> AddTokenAsync(string tokenName, byte[] tokenHash, long now)
    {
        await using var database = new DataConnection(options);
        var token = new AdminToken { TokenName = tokenName, TokenHash = tokenHash, CreatedTime = now };
        token.TokenId = Convert.ToUInt32(await database.InsertWithIdentityAsync(token));
        return token;
    }

    public async Task<AdminToken?> FindTokenAsync(byte[] tokenHash)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AdminToken>().FirstOrDefaultAsync(token => token.TokenHash == tokenHash);
    }

    public async Task TouchTokenAsync(uint tokenId, long now)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<AdminToken>()
            .Where(token => token.TokenId == tokenId)
            .Set(token => token.LastUsedTime, now)
            .UpdateAsync();
    }

    public async Task<AdminToken[]> ListTokensAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AdminToken>().OrderBy(token => token.TokenId).ToArrayAsync();
    }

    public async Task<bool> DeleteTokenAsync(uint tokenId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AdminToken>().Where(token => token.TokenId == tokenId).DeleteAsync() == 1;
    }

    public async Task AddAuditAsync(AdminAudit audit)
    {
        await using var database = new DataConnection(options);
        await database.InsertAsync(audit);
    }

    public async Task<AdminAudit[]> ListAuditAsync(ulong? beforeAuditId, int limit)
    {
        await using var database = new DataConnection(options);
        var audits = database.GetTable<AdminAudit>().AsQueryable();
        if (beforeAuditId is { } before)
        {
            audits = audits.Where(audit => audit.AuditId < before);
        }

        return await audits.OrderByDescending(audit => audit.AuditId).Take(limit).ToArrayAsync();
    }
}
