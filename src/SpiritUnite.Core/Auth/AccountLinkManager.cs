using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using SpiritUnite.Database;
using SpiritUnite.Database.Repositories;

namespace SpiritUnite.Auth;

public enum GuestLinkResult
{
    Linked,
    LoginExpired,
    ConfirmCodeInvalid,
    GuestNotFound,
    GuestAlreadyLinked,
    AccountNotFound,
    AccountRegistered,
    LockConflict,
}

public sealed record AccountLinkCheck(ulong AccountId, bool IsRegistered, string ConfirmCode, long ConfirmCodeExpireTime);

public sealed class AccountLinkManager(
    AuthorizationCodeStore codes,
    AccountRepository accounts,
    AccountSessionManager sessions,
    TimeProvider clock)
{
    public const int MaximumConfirmCodeLength = 128;
    private const int ConfirmCodeBytes = 32;
    private static readonly TimeSpan ConfirmCodeLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, PendingLink> pendingLinks = new(StringComparer.Ordinal);

    public async Task<AccountLinkCheck?> CheckAsync(string code, string? codeVerifier, LoginPlatform channel)
    {
        if (codes.Redeem(code, codeVerifier) is not { } grant || await accounts.FindByIdAsync(grant.AccountId) is not { IsGuest: false } account)
        {
            return null;
        }

        if (await accounts.IsRegisteredAsync(account.AccountId))
        {
            return new AccountLinkCheck(account.AccountId, true, string.Empty, 0);
        }

        RemoveExpired();
        var confirmCode = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(ConfirmCodeBytes));
        var expiresAt = clock.GetUtcNow() + ConfirmCodeLifetime;
        pendingLinks[confirmCode] = new PendingLink(account.AccountId, channel, expiresAt);
        return new AccountLinkCheck(account.AccountId, false, confirmCode, expiresAt.ToUnixTimeSeconds());
    }

    public async Task<(GuestLinkResult Result, ulong LinkedAccountId)> BindWithConfirmCodeAsync(
        string openId,
        string token,
        string confirmCode,
        LoginPlatform channel)
    {
        if (!AccountSessionManager.TryParseOpenId(openId, out var guestAccountId) ||
            await sessions.FindAccountIdAsync(token) != guestAccountId)
        {
            return (GuestLinkResult.LoginExpired, 0);
        }

        if (confirmCode.Length > MaximumConfirmCodeLength || !pendingLinks.TryRemove(confirmCode, out var pending) ||
            pending.ExpiresAt <= clock.GetUtcNow() || pending.Channel != channel)
        {
            return (GuestLinkResult.ConfirmCodeInvalid, 0);
        }

        try
        {
            return (await accounts.LinkGuestAsync(guestAccountId, pending.AccountId), pending.AccountId);
        }
        catch (Exception exception) when (DatabaseFaults.IsLockConflict(exception))
        {
            return (GuestLinkResult.LockConflict, pending.AccountId);
        }
    }

    private void RemoveExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var (confirmCode, pending) in pendingLinks)
        {
            if (pending.ExpiresAt <= now)
            {
                pendingLinks.TryRemove(confirmCode, out _);
            }
        }
    }

    private sealed record PendingLink(ulong AccountId, LoginPlatform Channel, DateTimeOffset ExpiresAt);
}
