using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Auth;
using AccountEntity = SpiritUNITE.Database.Models.Account;

namespace SpiritUNITE.Database.Repositories;

public sealed class AccountRepository(DataOptions options) : IAccountRepository
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
        var account = await database.GetTable<AccountEntity>().FirstOrDefaultAsync(account => account.UserName == userName);
        return account is null ? null : ToStoredAccount(account);
    }

    public async Task<StoredAccount?> FindByEmailAsync(string email)
    {
        await using var database = new DataConnection(options);
        var account = await database.GetTable<AccountEntity>().FirstOrDefaultAsync(account => account.Email == email);
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
                PasswordIterations = password.Iterations,
            }));
            return new AccountCreation(AccountRegistrationResult.Created, new PlayerAccount(accountId, userName));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return new AccountCreation(await FindTakenAsync(database, userName, email) ?? AccountRegistrationResult.UserNameTaken, null);
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

    public async Task UpdatePasswordAsync(ulong accountId, AccountPassword password)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<AccountEntity>()
            .Where(account => account.AccountId == accountId)
            .Set(account => account.PasswordHash, password.Hash)
            .Set(account => account.PasswordSalt, password.Salt)
            .Set(account => account.PasswordIterations, password.Iterations)
            .UpdateAsync();
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

    private static StoredAccount ToStoredAccount(AccountEntity account) => new(
        new PlayerAccount(account.AccountId, account.UserName),
        new AccountPassword(account.PasswordHash, account.PasswordSalt, account.PasswordIterations));
}
