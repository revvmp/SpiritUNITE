namespace SpiritUNITE.Auth;

public sealed record PlayerAccount(ulong AccountId, string? UserName)
{
    public bool IsGuest => UserName is null;
}

public sealed record StoredAccount(PlayerAccount Account, AccountPassword Password);
