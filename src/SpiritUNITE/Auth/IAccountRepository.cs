namespace SpiritUNITE.Auth;

public sealed record PlayerAccount(ulong AccountId, string? UserName)
{
    public bool IsGuest => UserName is null;
}

public sealed record StoredAccount(PlayerAccount Account, AccountPassword Password);

public sealed record AccountCreation(AccountRegistrationResult Result, PlayerAccount? Account);

public enum AccountRegistrationResult
{
    Created,
    UserNameInvalid,
    EmailInvalid,
    PasswordInvalid,
    PasswordConfirmationMismatch,
    UserNameTaken,
    EmailTaken,
    Busy,
}

public interface IAccountRepository
{
    Task<PlayerAccount?> FindByIdAsync(ulong accountId);

    Task<StoredAccount?> FindByUserNameAsync(string userName);

    Task<StoredAccount?> FindByEmailAsync(string email);

    Task<AccountCreation> CreateAsync(string userName, string email, AccountPassword password);

    Task<PlayerAccount?> FindGuestAsync(byte[] guestIdHash);

    Task<PlayerAccount?> CreateGuestAsync(byte[] guestIdHash);

    Task RecordLoginAsync(ulong accountId);

    Task UpdatePasswordAsync(ulong accountId, AccountPassword password);
}
