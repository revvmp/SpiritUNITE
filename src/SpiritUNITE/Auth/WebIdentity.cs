using System.Text;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Auth;

public sealed class WebIdentity(AccountSessionManager sessions, AccountManager accounts, AccountRepository accountRepository, TimeProvider clock)
{
    public const int MaximumItopEncodeParamLength = 2048;
    private const string GOpenIdField = "gopenid";
    private const string AccessTokenField = "access_token";
    private const char FieldSeparator = '&';
    private const char ValueSeparator = '=';

    private static readonly byte[] ItopEncodeParamKey = "itopenckey123456"u8.ToArray();

    public async Task<ulong?> FindWebViewAccountIdAsync(string? roleId, string? itopEncodeParam)
    {
        if (roleId is null || itopEncodeParam is null || itopEncodeParam.Length > MaximumItopEncodeParamLength ||
            !AccountSessionManager.TryParseOpenId(roleId, out var roleAccountId) ||
            ReadItopEncodeParam(itopEncodeParam) is not { } fields ||
            !fields.TryGetValue(GOpenIdField, out var gOpenId) || gOpenId != roleId ||
            !fields.TryGetValue(AccessTokenField, out var accessToken))
        {
            return null;
        }

        return await sessions.FindAccountIdAsync(accessToken) == roleAccountId ? roleAccountId : null;
    }

    public async Task<ulong?> FindLoginAccountIdAsync(string login, string password)
    {
        if (await accounts.AuthenticateAsync(login, password) is not { } account)
        {
            return null;
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        return await accountRepository.FindBanAsync(account.AccountId) is { } ban && ban.IsActive(now) ? null : account.AccountId;
    }

    private static Dictionary<string, string>? ReadItopEncodeParam(string hex)
    {
        if (hex.Length % 2 != 0 || !hex.All(char.IsAsciiHexDigit) ||
            TeaCipher.Decrypt(Convert.FromHexString(hex), ItopEncodeParamKey) is not { } plain)
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in Encoding.UTF8.GetString(plain).Split(FieldSeparator))
        {
            var separator = field.IndexOf(ValueSeparator, StringComparison.Ordinal);
            if (separator <= 0 || !fields.TryAdd(field[..separator], field[(separator + 1)..]))
            {
                return null;
            }
        }

        return fields;
    }
}
