using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using SpiritUNITE.Database.Repositories;

namespace SpiritUNITE.Auth;

public enum AccountLoginResult
{
    LoggedIn,
    Refused,
    Throttled,
    Busy,
}

public sealed record AccountLogin(AccountLoginResult Result, PlayerAccount? Account);

public sealed class AccountManager(AccountRepository repository, PasswordHashing hashing, LoginThrottle throttle)
{
    public const int MinimumUserNameLength = 3;
    public const int MaximumUserNameLength = 20;
    public const int MaximumEmailLength = 254;
    public const int MinimumPasswordLength = 8;
    public const int MaximumPasswordLength = 128;

    private static readonly AccountPassword UnknownAccountPassword =
        AccountPassword.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(MaximumUserNameLength)));

    private static readonly AccountLogin Refused = new(AccountLoginResult.Refused, null);
    private static readonly AccountLogin Throttled = new(AccountLoginResult.Throttled, null);
    private static readonly AccountLogin Busy = new(AccountLoginResult.Busy, null);

    public async Task<AccountCreation> RegisterAsync(
        string userName,
        string email,
        string password,
        string passwordConfirmation,
        CancellationToken cancellationToken)
    {
        var trimmedUserName = userName.Trim();
        var normalizedEmail = NormalizeLogin(email);
        var refusal = ValidateRegistration(trimmedUserName, normalizedEmail, password, passwordConfirmation);
        if (refusal is { } result)
        {
            return new AccountCreation(result, null);
        }

        if (await hashing.TryCreateAsync(password, cancellationToken) is not { } accountPassword)
        {
            return new AccountCreation(AccountRegistrationResult.Busy, null);
        }

        return await repository.CreateAsync(trimmedUserName, normalizedEmail, accountPassword);
    }

    public async Task<AccountLogin> AuthenticateAsync(
        string userNameOrEmail,
        string password,
        IPAddress? clientAddress,
        CancellationToken cancellationToken)
    {
        var login = NormalizeLogin(userNameOrEmail);
        if (login.Length is 0 or > MaximumEmailLength || password.Length is 0 or > MaximumPasswordLength)
        {
            return Refused;
        }

        if (await FindAsync(login) is not { } stored)
        {
            var unknownCheck = await hashing.CheckAsync(UnknownAccountPassword, password, cancellationToken);
            return unknownCheck == PasswordCheck.Busy ? Busy : Refused;
        }

        if (throttle.TryBeginAttempt(stored.Account.AccountId, clientAddress) is not { } attempt)
        {
            return Throttled;
        }

        await throttle.DelayIfAttackedAsync(attempt, cancellationToken);
        var check = await hashing.CheckAsync(stored.Password, password, cancellationToken);
        if (check == PasswordCheck.Busy)
        {
            throttle.Abandon(attempt);
            return Busy;
        }

        if (check == PasswordCheck.Mismatched)
        {
            throttle.RecordFailure(attempt);
            return Refused;
        }

        throttle.RecordSuccess(attempt);
        await UpgradePasswordAsync(stored, password, cancellationToken);
        await repository.RecordLoginAsync(stored.Account.AccountId);
        return new AccountLogin(AccountLoginResult.LoggedIn, stored.Account);
    }

    private async Task<StoredAccount?> FindAsync(string login)
    {
        if (IsValidUserName(login))
        {
            return await repository.FindByUserNameAsync(login);
        }

        return IsValidEmail(login) ? await repository.FindByEmailAsync(login) : null;
    }

    private async Task UpgradePasswordAsync(StoredAccount stored, string password, CancellationToken cancellationToken)
    {
        if (stored.Password.IsOutdated && await hashing.TryCreateAsync(password, cancellationToken) is { } upgraded)
        {
            await repository.UpdatePasswordAsync(stored.Account.AccountId, upgraded);
        }
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
