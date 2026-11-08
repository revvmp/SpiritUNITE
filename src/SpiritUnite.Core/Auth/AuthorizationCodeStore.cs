using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SpiritUnite.Auth;

public sealed record AuthorizationGrant(ulong AccountId, bool IsNewAccount, AuthorizationRequest Request, DateTimeOffset ExpiresAt);

public sealed class AuthorizationCodeStore(TimeProvider clock)
{
    private const int CodeBytes = 32;
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, AuthorizationGrant> grants = new(StringComparer.Ordinal);

    public string Issue(ulong accountId, bool isNewAccount, AuthorizationRequest request)
    {
        RemoveExpired();
        var code = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(CodeBytes));
        grants[code] = new AuthorizationGrant(accountId, isNewAccount, request, clock.GetUtcNow() + CodeLifetime);
        return code;
    }

    public AuthorizationGrant? Redeem(string code, string? codeVerifier)
    {
        if (!grants.TryRemove(code, out var grant) || grant.ExpiresAt <= clock.GetUtcNow())
        {
            return null;
        }

        return grant.Request.AcceptsVerifier(codeVerifier) ? grant : null;
    }

    private void RemoveExpired()
    {
        var now = clock.GetUtcNow();
        foreach (var (code, grant) in grants)
        {
            if (grant.ExpiresAt <= now)
            {
                grants.TryRemove(code, out _);
            }
        }
    }
}
