using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SpiritUNITE.Auth;

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

        return VerifierMatches(grant.Request, codeVerifier) ? grant : null;
    }

    private static bool VerifierMatches(AuthorizationRequest request, string? codeVerifier)
    {
        if (codeVerifier is null || !AuthorizationRequest.IsValidCodeVerifier(codeVerifier))
        {
            return false;
        }

        var expected = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(request.CodeChallenge));
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
