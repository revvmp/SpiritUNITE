using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using MySqlConnector;
using SpiritUnite.Auth;
using SpiritUnite.Database.Models;
using AccountEntity = SpiritUnite.Database.Models.Account;

namespace SpiritUnite.Database.Repositories;

public sealed class AccountRepository(DataOptions options)
{
    public async Task<PlayerAccount?> FindByIdAsync(ulong accountId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountEntity>()
            .Where(account => account.AccountId == accountId)
            .Select(account => new PlayerAccount(account.AccountId, account.UserName, account.GuestIdHash != null))
            .FirstOrDefaultAsync();
    }

    public async Task<StoredAccount?> FindByUserNameAsync(string userName)
    {
        await using var database = new DataConnection(options);
        var account = await database.GetTable<AccountEntity>()
            .FirstOrDefaultAsync(account => account.UserName == userName);
        return account is null ? null : ToStoredAccount(account);
    }

    public async Task<StoredAccount?> FindByEmailAsync(string email)
    {
        await using var database = new DataConnection(options);
        var account = await database.GetTable<AccountEntity>()
            .FirstOrDefaultAsync(account => account.Email == email);
        return account is null ? null : ToStoredAccount(account);
    }

    public async Task<AccountCreation> CreateAsync(string userName, string email, AccountPassword password)
    {
        await using var database = new DataConnection(options);
        var taken = await FindTakenAsync(database, userName, email);
        if (taken is { } takenResult)
        {
            return new AccountCreation(takenResult, null);
        }

        try
        {
            var accountId = Convert.ToUInt64(await database.InsertWithIdentityAsync(new AccountEntity
            {
                UserName = userName,
                Email = email,
                PasswordHash = password.Hash,
                PasswordSalt = password.Salt,
            }));
            return new AccountCreation(AccountRegistrationResult.Created, new PlayerAccount(accountId, userName, false));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return new AccountCreation(await FindTakenAsync(database, userName, email) ?? AccountRegistrationResult.UserNameTaken, null);
        }
    }

    public async Task<PlayerAccount?> FindGuestAsync(byte[] guestIdHash)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountEntity>()
            .Where(account => account.GuestIdHash == guestIdHash)
            .Select(account => new PlayerAccount(account.AccountId, null, true))
            .FirstOrDefaultAsync();
    }

    public async Task<PlayerAccount?> CreateGuestAsync(byte[] guestIdHash)
    {
        await using var database = new DataConnection(options);
        try
        {
            var accountId = Convert.ToUInt64(await database.InsertWithIdentityAsync(new AccountEntity { GuestIdHash = guestIdHash }));
            return new PlayerAccount(accountId, null, true);
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return null;
        }
    }

    public async Task<bool> IsRegisteredAsync(ulong accountId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountEntity>().AnyAsync(account => account.AccountId == accountId && account.GuestIdHash != null) ||
            await database.GetTable<Role>().AnyAsync(role => role.GameUlongUID == accountId);
    }

    public async Task<GuestLinkResult> LinkGuestAsync(ulong guestAccountId, ulong webAccountId)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var locked = await database.GetTable<AccountEntity>()
            .Where(account => account.AccountId == guestAccountId || account.AccountId == webAccountId)
            .OrderBy(account => account.AccountId)
            .AsMySql()
            .ForUpdateHint()
            .ToArrayAsync();
        var guest = locked.FirstOrDefault(account => account.AccountId == guestAccountId);
        var web = locked.FirstOrDefault(account => account.AccountId == webAccountId);
        var refusal = await FindLinkRefusalAsync(database, guest, web);
        if (refusal is not null || web is null)
        {
            await transaction.RollbackAsync();
            return refusal ?? GuestLinkResult.AccountNotFound;
        }

        await database.GetTable<AccountEntity>().Where(account => account.AccountId == webAccountId).DeleteAsync();
        await database.GetTable<AccountEntity>()
            .Where(account => account.AccountId == guestAccountId)
            .Set(account => account.UserName, web.UserName)
            .Set(account => account.Email, web.Email)
            .Set(account => account.PasswordHash, web.PasswordHash)
            .Set(account => account.PasswordSalt, web.PasswordSalt)
            .UpdateAsync();
        await transaction.CommitAsync();
        return GuestLinkResult.Linked;
    }

    public async Task RecordLoginAsync(ulong accountId)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<AccountEntity>()
            .Where(account => account.AccountId == accountId)
            .Set(account => account.LastLoginAt, () => Sql.CurrentTimestamp)
            .UpdateAsync();
    }

    public async Task<bool> DeleteAsync(ulong accountId)
    {
        await using var database = new DataConnection(options);
        var deleted = await database.GetTable<AccountEntity>()
            .Where(account => account.AccountId == accountId)
            .DeleteAsync();
        return deleted == 1;
    }

    public async Task<AccountProfile?> FindProfileAsync(ulong accountId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountEntity>()
            .Where(account => account.AccountId == accountId)
            .Select(account => new AccountProfile(account.AccountId, account.UserName, account.Email, account.LastLoginAt))
            .FirstOrDefaultAsync();
    }

    public async Task<AccountProfile[]> SearchProfilesAsync(string text, int limit)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountEntity>()
            .Where(account => account.UserName!.Contains(text) || account.Email!.Contains(text))
            .OrderBy(account => account.AccountId)
            .Take(limit)
            .Select(account => new AccountProfile(account.AccountId, account.UserName, account.Email, account.LastLoginAt))
            .ToArrayAsync();
    }

    public async Task<AccountProfile[]> ListProfilesAsync(int skip, int take)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountEntity>()
            .OrderByDescending(account => account.AccountId)
            .Skip(skip)
            .Take(take)
            .Select(account => new AccountProfile(account.AccountId, account.UserName, account.Email, account.LastLoginAt))
            .ToArrayAsync();
    }

    public async Task<int> CountAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountEntity>().CountAsync();
    }

    public async Task<AccountBanState?> FindBanAsync(ulong accountId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountBan>()
            .Where(ban => ban.AccountId == accountId)
            .Select(ban => new AccountBanState(ban.BanEndTime, ban.Reason))
            .FirstOrDefaultAsync();
    }

    public async Task SaveBanAsync(ulong accountId, AccountBanState ban)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(new AccountBan { AccountId = accountId, BanEndTime = ban.BanEndTime, Reason = ban.Reason });
    }

    public async Task<bool> DeleteBanAsync(ulong accountId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountBan>().Where(ban => ban.AccountId == accountId).DeleteAsync() == 1;
    }

    private static async Task<AccountRegistrationResult?> FindTakenAsync(DataConnection database, string userName, string email)
    {
        var accounts = database.GetTable<AccountEntity>();
        if (await accounts.AnyAsync(account => account.UserName == userName))
        {
            return AccountRegistrationResult.UserNameTaken;
        }

        return await accounts.AnyAsync(account => account.Email == email) ? AccountRegistrationResult.EmailTaken : null;
    }

    private static async Task<GuestLinkResult?> FindLinkRefusalAsync(DataConnection database, AccountEntity? guest, AccountEntity? web)
    {
        if (guest is null)
        {
            return GuestLinkResult.GuestNotFound;
        }

        if (guest.GuestIdHash is null || guest.UserName is not null)
        {
            return GuestLinkResult.GuestAlreadyLinked;
        }

        if (web is not { UserName: not null, Email: not null, PasswordHash: not null, PasswordSalt: not null })
        {
            return GuestLinkResult.AccountNotFound;
        }

        return web.GuestIdHash is not null || await HasRoleForUpdateAsync(database, web.AccountId)
            ? GuestLinkResult.AccountRegistered
            : null;
    }

    private static async Task<bool> HasRoleForUpdateAsync(DataConnection database, ulong accountId) =>
        (await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == accountId)
            .Select(role => role.GameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .ToArrayAsync()).Length > 0;

    private static StoredAccount? ToStoredAccount(AccountEntity account) =>
        account is { UserName: { } userName, PasswordHash: { } hash, PasswordSalt: { } salt }
            ? new StoredAccount(new PlayerAccount(account.AccountId, userName, account.GuestIdHash != null), new AccountPassword(hash, salt))
            : null;
}
