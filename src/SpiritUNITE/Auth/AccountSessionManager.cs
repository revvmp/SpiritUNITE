using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SpiritUNITE.Database.Repositories;

namespace SpiritUNITE.Auth;

public sealed record AccountSession(PlayerAccount Account, string Token, long ExpiresAt, bool IsNewAccount);

public sealed class AccountSessionManager(
    AuthorizationCodeStore codes,
    AccountRepository accounts,
    AccountTokenRepository tokens,
    TimeProvider clock)
{
    public const int MaximumTokenLength = 128;
    private const int TokenBytes = 32;
    private const string GuestIdFormat = "D";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

    public static string ToOpenId(ulong accountId) => accountId.ToString(CultureInfo.InvariantCulture);

    public static bool TryParseOpenId(string openId, out ulong accountId) =>
        ulong.TryParse(openId, NumberStyles.None, CultureInfo.InvariantCulture, out accountId) && accountId != 0;

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public async Task<AccountSession?> ExchangeCodeAsync(string code, string? codeVerifier)
    {
        if (codes.Redeem(code, codeVerifier) is not { } grant || await accounts.FindByIdAsync(grant.AccountId) is not { } account ||
            account.IsGuest)
        {
            return null;
        }

        return await IssueAsync(account, grant.IsNewAccount);
    }

    public async Task<AccountSession?> GuestLoginAsync(string guestId)
    {
        if (!Guid.TryParseExact(guestId, GuestIdFormat, out var parsedGuestId) || parsedGuestId == Guid.Empty)
        {
            return null;
        }

        var guestIdHash = SHA256.HashData(Encoding.ASCII.GetBytes(parsedGuestId.ToString(GuestIdFormat)));
        if (await accounts.FindGuestAsync(guestIdHash) is { } returningGuest)
        {
            return await ResumeGuestAsync(returningGuest);
        }

        if (await accounts.CreateGuestAsync(guestIdHash) is { } newGuest)
        {
            await accounts.RecordLoginAsync(newGuest.AccountId);
            return await IssueAsync(newGuest, isNewAccount: true);
        }

        var racedGuest = await accounts.FindGuestAsync(guestIdHash)
            ?? throw new InvalidOperationException("Guest account that already exists could not be loaded");
        return await ResumeGuestAsync(racedGuest);
    }

    public async Task<ulong?> FindAccountIdAsync(string token)
    {
        if (!IsValidTokenLength(token))
        {
            return null;
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        return await tokens.FindAccountIdAsync(HashToken(token), now) is { } accountId && !await IsBannedAsync(accountId, now)
            ? accountId
            : null;
    }

    private async Task<bool> IsBannedAsync(ulong accountId, long now) =>
        await accounts.FindBanAsync(accountId) is { } ban && ban.IsActive(now);

    public async Task<AccountSession?> ResumeAsync(string openId, string token)
    {
        if (!TryParseOpenId(openId, out var accountId) || !IsValidTokenLength(token))
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var expiresAt = ExpiryFrom(now);
        if (!await tokens.ExtendAsync(HashToken(token), accountId, now.ToUnixTimeSeconds(), expiresAt) ||
            await accounts.FindByIdAsync(accountId) is not { } account)
        {
            return null;
        }

        return new AccountSession(account, token, expiresAt, false);
    }

    public async Task<bool> LogoutAsync(string openId, string token) =>
        TryParseOpenId(openId, out var accountId) && IsValidTokenLength(token) &&
        await tokens.RevokeAsync(HashToken(token), accountId);

    private static bool IsValidTokenLength(string token) => token.Length is > 0 and <= MaximumTokenLength;

    private static long ExpiryFrom(DateTimeOffset now) => (now + TokenLifetime).ToUnixTimeSeconds();

    private async Task<AccountSession> ResumeGuestAsync(PlayerAccount guest)
    {
        await accounts.RecordLoginAsync(guest.AccountId);
        return await IssueAsync(guest, isNewAccount: false);
    }

    private async Task<AccountSession> IssueAsync(PlayerAccount account, bool isNewAccount)
    {
        var now = clock.GetUtcNow();
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var expiresAt = ExpiryFrom(now);
        await tokens.RemoveExpiredAsync(account.AccountId, now.ToUnixTimeSeconds());
        await tokens.AddAsync(HashToken(token), account.AccountId, expiresAt);
        return new AccountSession(account, token, expiresAt, isNewAccount);
    }
}
