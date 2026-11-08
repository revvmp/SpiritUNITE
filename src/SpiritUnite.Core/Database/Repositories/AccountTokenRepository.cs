using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Auth;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public sealed class AccountTokenRepository(DataOptions options)
{
    public async Task AddAsync(byte[] tokenHash, ulong accountId, long expiresAt)
    {
        await using var database = new DataConnection(options);
        await database.InsertAsync(new AccountToken
        {
            TokenHash = tokenHash,
            AccountId = accountId,
            ExpiresAt = expiresAt,
        });
    }

    public async Task<bool> ExtendAsync(byte[] tokenHash, ulong accountId, long now, long expiresAt)
    {
        await using var database = new DataConnection(options);
        var extended = await database.GetTable<AccountToken>()
            .Where(token => token.TokenHash == tokenHash && token.AccountId == accountId && token.ExpiresAt > now)
            .Set(token => token.ExpiresAt, expiresAt)
            .UpdateAsync();
        return extended == 1;
    }

    public async Task<ulong?> FindAccountIdAsync(byte[] tokenHash, long now)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountToken>()
            .Where(token => token.TokenHash == tokenHash && token.ExpiresAt > now)
            .Select(token => (ulong?)token.AccountId)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> RevokeAsync(byte[] tokenHash, ulong accountId)
    {
        await using var database = new DataConnection(options);
        var revoked = await database.GetTable<AccountToken>()
            .Where(token => token.TokenHash == tokenHash && token.AccountId == accountId)
            .DeleteAsync();
        return revoked == 1;
    }

    public async Task RemoveExpiredAsync(ulong accountId, long now)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<AccountToken>()
            .Where(token => token.AccountId == accountId && token.ExpiresAt <= now)
            .DeleteAsync();
    }
}
