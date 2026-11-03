using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SpiritUNITE.Auth;

public sealed record AccountSession(PlayerAccount Account, string Token, long ExpiresAt, bool IsNewAccount);

public sealed class AccountSessionService(
    AuthorizationCodeStore codes,
    IAccountRepository accounts,
    IAccountTokenRepository tokens,
    TimeProvider clock)
{
    public const int MaximumTokenLength = 128;
    private const int TokenBytes = 32;
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(30);

    public static string ToOpenId(ulong accountId) => accountId.ToString(CultureInfo.InvariantCulture);

    public static bool TryParseOpenId(string openId, out ulong accountId) =>
        ulong.TryParse(openId, NumberStyles.None, CultureInfo.InvariantCulture, out accountId) && accountId != 0;

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public async Task<AccountSession?> ExchangeCodeAsync(string code, string? codeVerifier)
    {
        if (codes.Redeem(code, codeVerifier) is not { } grant || await accounts.FindByIdAsync(grant.AccountId) is not { } account)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        var expiresAt = (now + TokenLifetime).ToUnixTimeSeconds();
        await tokens.RemoveExpiredAsync(account.AccountId, now.ToUnixTimeSeconds());
        await tokens.AddAsync(HashToken(token), account.AccountId, expiresAt);
        return new AccountSession(account, token, expiresAt, grant.IsNewAccount);
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
}
