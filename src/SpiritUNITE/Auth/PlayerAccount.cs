namespace SpiritUNITE.Auth;

public sealed record PlayerAccount(ulong AccountId, string? UserName)
{
    public bool IsGuest => UserName is null;
}

public sealed record StoredAccount(PlayerAccount Account, AccountPassword Password);

public sealed record AccountProfile(ulong AccountId, string? UserName, string? Email, DateTime? LastLoginAt)
{
    public bool IsGuest => UserName is null;
}

public sealed record AccountBanState(long BanEndTime, string Reason)
{
    public const long Permanent = 0;

    public bool IsActive(long now) => BanEndTime == Permanent || BanEndTime > now;
}
