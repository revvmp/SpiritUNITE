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
