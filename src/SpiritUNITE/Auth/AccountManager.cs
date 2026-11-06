using System.Net.Mail;
using System.Text;
using SpiritUNITE.Database.Repositories;

namespace SpiritUNITE.Auth;

public enum AccountRegistrationResult
{
    Created,
    UserNameInvalid,
    EmailInvalid,
    PasswordInvalid,
    PasswordConfirmationMismatch,
    UserNameTaken,
    EmailTaken,
}

public sealed record AccountCreation(AccountRegistrationResult Result, PlayerAccount? Account);

public sealed class AccountManager(AccountRepository repository)
{
    public const int MinimumUserNameLength = 3;
    public const int MaximumUserNameLength = 20;
    public const int MaximumEmailLength = 254;
    public const int MinimumPasswordLength = 8;
    public const int MaximumPasswordLength = 128;

    public async Task<AccountCreation> RegisterAsync(
        string userName,
        string email,
        string password,
        string passwordConfirmation)
    {
        var trimmedUserName = userName.Trim();
        var normalizedEmail = NormalizeLogin(email);
        var refusal = ValidateRegistration(trimmedUserName, normalizedEmail, password, passwordConfirmation);
        if (refusal is { } result)
        {
            return new AccountCreation(result, null);
        }

        return await repository.CreateAsync(trimmedUserName, normalizedEmail, AccountPassword.Create(password));
    }

    public async Task<PlayerAccount?> AuthenticateAsync(string userNameOrEmail, string password)
    {
        var login = NormalizeLogin(userNameOrEmail);
        if (login.Length is 0 or > MaximumEmailLength || password.Length is 0 or > MaximumPasswordLength ||
            await FindAsync(login) is not { } stored || !stored.Password.Matches(password))
        {
            return null;
        }

        await repository.RecordLoginAsync(stored.Account.AccountId);
        return stored.Account;
    }

    public Task<bool> DeleteAsync(ulong accountId) => repository.DeleteAsync(accountId);

    public async Task<LoginPlatform[]> FindBoundChannelsAsync(ulong accountId) =>
        await repository.FindByIdAsync(accountId) is { } account
            ? [account.IsGuest ? LoginPlatform.GUEST : LoginPlatform.POKEMON]
            : [];

    private async Task<StoredAccount?> FindAsync(string login)
    {
        if (IsValidUserName(login))
        {
            return await repository.FindByUserNameAsync(login);
        }

        return IsValidEmail(login) ? await repository.FindByEmailAsync(login) : null;
    }

    private static AccountRegistrationResult? ValidateRegistration(string userName, string email, string password, string passwordConfirmation)
    {
        if (!IsValidUserName(userName))
        {
            return AccountRegistrationResult.UserNameInvalid;
        }

        if (!IsValidEmail(email))
        {
            return AccountRegistrationResult.EmailInvalid;
        }

        if (password.Length is < MinimumPasswordLength or > MaximumPasswordLength)
        {
            return AccountRegistrationResult.PasswordInvalid;
        }

        return password == passwordConfirmation ? null : AccountRegistrationResult.PasswordConfirmationMismatch;
    }

    private static bool IsValidUserName(string userName) =>
        userName.Length is >= MinimumUserNameLength and <= MaximumUserNameLength &&
        userName.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.');

    private static bool IsValidEmail(string email) =>
        email.Length <= MaximumEmailLength &&
        Ascii.IsValid(email) &&
        MailAddress.TryCreate(email, out var address) &&
        address.Address == email &&
        address.Host.Contains('.');

    private static string NormalizeLogin(string login) => login.Trim().ToLowerInvariant();
}
