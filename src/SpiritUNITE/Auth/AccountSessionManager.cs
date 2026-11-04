using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Network;

namespace SpiritUNITE.Auth;

public sealed record AccountSession(PlayerAccount Account, string Token, long ExpiresAt, bool IsNewAccount);

public enum GuestLoginResult
{
    LoggedIn,
    GuestIdInvalid,
    Throttled,
}

public sealed record GuestLogin(GuestLoginResult Result, AccountSession? Session);

public sealed class AccountSessionManager(
    AuthorizationCodeStore codes,
    AccountRepository accounts,
    AccountTokenRepository tokens,
    TimeProvider clock)
{
    public const int MaximumTokenLength = 128;
    private const int TokenBytes = 32;
    private const string GuestIdFormat = "D";
    private const int MaximumGuestCreationsPerClient = 10;
    private const int MaximumTrackedGuestClients = 100_000;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan GuestCreationWindow = TimeSpan.FromHours(1);
    private static readonly GuestLogin GuestIdInvalid = new(GuestLoginResult.GuestIdInvalid, null);
    private static readonly GuestLogin GuestCreationThrottled = new(GuestLoginResult.Throttled, null);

    private readonly FailureWindows<string> guestCreations = new(clock, GuestCreationWindow, MaximumTrackedGuestClients);

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

    public async Task<GuestLogin> GuestLoginAsync(string guestId, IPAddress? clientAddress)
    {
        if (!Guid.TryParseExact(guestId, GuestIdFormat, out var parsedGuestId) || parsedGuestId == Guid.Empty)
        {
            return GuestIdInvalid;
        }

        var guestIdHash = SHA256.HashData(Encoding.ASCII.GetBytes(parsedGuestId.ToString(GuestIdFormat)));
        if (await accounts.FindGuestAsync(guestIdHash) is { } returningGuest)
        {
            return await ResumeGuestAsync(returningGuest);
        }

        var clientPartition = ClientAddress.Partition(clientAddress);
        if (!guestCreations.TryAdd(clientPartition, MaximumGuestCreationsPerClient))
        {
            return GuestCreationThrottled;
        }

        if (await accounts.CreateGuestAsync(guestIdHash) is { } newGuest)
        {
            await accounts.RecordLoginAsync(newGuest.AccountId);
            return new GuestLogin(GuestLoginResult.LoggedIn, await IssueAsync(newGuest, isNewAccount: true));
        }

        guestCreations.Release(clientPartition);
        var racedGuest = await accounts.FindGuestAsync(guestIdHash)
            ?? throw new InvalidOperationException("Guest account that already exists could not be loaded");
        return await ResumeGuestAsync(racedGuest);
    }

    public async Task<ulong?> FindAccountIdAsync(string token) =>
        token.Length is 0 or > MaximumTokenLength
            ? null
            : await tokens.FindAccountIdAsync(HashToken(token), clock.GetUtcNow().ToUnixTimeSeconds());

    public async Task<AccountSession?> ResumeAsync(string openId, string token)
    {
        if (!TryParseOpenId(openId, out var accountId) || token.Length is 0 or > MaximumTokenLength)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var expiresAt = (now + TokenLifetime).ToUnixTimeSeconds();
        if (!await tokens.ExtendAsync(HashToken(token), accountId, now.ToUnixTimeSeconds(), expiresAt) ||
            await accounts.FindByIdAsync(accountId) is not { } account)
        {
            return null;
        }

        return new AccountSession(account, token, expiresAt, false);
    }

    public async Task<bool> LogoutAsync(string openId, string token) =>
        TryParseOpenId(openId, out var accountId) && token.Length is > 0 and <= MaximumTokenLength &&
        await tokens.RevokeAsync(HashToken(token), accountId);

    private async Task<GuestLogin> ResumeGuestAsync(PlayerAccount guest)
    {
        await accounts.RecordLoginAsync(guest.AccountId);
        return new GuestLogin(GuestLoginResult.LoggedIn, await IssueAsync(guest, isNewAccount: false));
    }

    private async Task<AccountSession> IssueAsync(PlayerAccount account, bool isNewAccount)
    {
        var now = clock.GetUtcNow();
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var expiresAt = (now + TokenLifetime).ToUnixTimeSeconds();
        await tokens.RemoveExpiredAsync(account.AccountId, now.ToUnixTimeSeconds());
        await tokens.AddAsync(HashToken(token), account.AccountId, expiresAt);
        return new AccountSession(account, token, expiresAt, isNewAccount);
    }
}
