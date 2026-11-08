using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Auth;
using SpiritUNITE.Database.Models;
using AccountEntity = SpiritUNITE.Database.Models.Account;

namespace SpiritUNITE.Database.Repositories;

public sealed class AccountRepository(DataOptions options)
{
    public async Task<PlayerAccount?> FindByIdAsync(ulong accountId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<AccountEntity>()
            .Where(account => account.AccountId == accountId)
            .Select(account => new PlayerAccount(account.AccountId, account.UserName))
            .FirstOrDefaultAsync();
    }

    public async Task<StoredAccount?> FindByUserNameAsync(string userName)
    {
        await using var database = new DataConnection(options);
        var account = await database.GetTable<AccountEntity>()
            .FirstOrDefaultAsync(account => account.UserName == userName && account.GuestIdHash == null);
        return account is null ? null : ToStoredAccount(account);
    }

    public async Task<StoredAccount?> FindByEmailAsync(string email)
    {
        await using var database = new DataConnection(options);
        var account = await database.GetTable<AccountEntity>()
            .FirstOrDefaultAsync(account => account.Email == email && account.GuestIdHash == null);
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
            return new AccountCreation(AccountRegistrationResult.Created, new PlayerAccount(accountId, userName));
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
            .Select(account => new PlayerAccount(account.AccountId, null))
            .FirstOrDefaultAsync();
    }

    public async Task<PlayerAccount?> CreateGuestAsync(byte[] guestIdHash)
    {
        await using var database = new DataConnection(options);
        try
        {
            var accountId = Convert.ToUInt64(await database.InsertWithIdentityAsync(new AccountEntity { GuestIdHash = guestIdHash }));
            return new PlayerAccount(accountId, null);
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return null;
        }
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

    private static StoredAccount? ToStoredAccount(AccountEntity account) =>
        account is { UserName: { } userName, PasswordHash: { } hash, PasswordSalt: { } salt }
            ? new StoredAccount(new PlayerAccount(account.AccountId, userName), new AccountPassword(hash, salt))
            : null;
}
