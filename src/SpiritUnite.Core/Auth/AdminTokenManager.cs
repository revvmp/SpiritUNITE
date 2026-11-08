using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;

namespace SpiritUnite.Auth;

public sealed record AdminIdentity(uint TokenId, string TokenName);

public sealed record AdminTokenInfo(uint TokenId, string TokenName, long CreatedTime, long LastUsedTime);

public sealed record IssuedAdminToken(AdminTokenInfo Info, string Token);

public sealed class AdminTokenManager(AdminRepository repository, TimeProvider clock, ILogger<AdminTokenManager> logger)
{
    public const int MaximumTokenNameLength = 40;
    public const uint StartupTokenId = 0;
    public const string StartupTokenName = "Startup";
    private const string TokenPrefix = "su_";
    private const int TokenBytes = 32;
    private const int MaximumTokenLength = 64;
    private static readonly TimeSpan LastUsedPrecision = TimeSpan.FromMinutes(1);

    private readonly string startupToken = NewToken();

    public string StartupToken => startupToken;

    private byte[] StartupTokenHash => field ??= Hash(startupToken);

    public async Task<AdminIdentity?> AuthenticateAsync(string token)
    {
        if (token.Length is 0 or > MaximumTokenLength)
        {
            return null;
        }

        var tokenHash = Hash(token);
        if (CryptographicOperations.FixedTimeEquals(tokenHash, StartupTokenHash))
        {
            return new AdminIdentity(StartupTokenId, StartupTokenName);
        }

        if (await repository.FindTokenAsync(tokenHash) is not { } stored)
        {
            return null;
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (now - stored.LastUsedTime >= (long)LastUsedPrecision.TotalSeconds)
        {
            await repository.TouchTokenAsync(stored.TokenId, now);
        }

        return new AdminIdentity(stored.TokenId, stored.TokenName);
    }

    public static string? NormalizeTokenName(string? tokenName) =>
        tokenName?.Trim() is { Length: > 0 and <= MaximumTokenNameLength } trimmed && !trimmed.Any(char.IsControl) ? trimmed : null;

    public async Task<IssuedAdminToken?> CreateAsync(string? tokenName)
    {
        if (NormalizeTokenName(tokenName) is not { } name)
        {
            return null;
        }

        var token = NewToken();
        var stored = await repository.AddTokenAsync(name, Hash(token), clock.GetUtcNow().ToUnixTimeSeconds());
        logger.LogInformation("Admin token {TokenId} ({TokenName}) created, {Length} characters", stored.TokenId, stored.TokenName, token.Length);
        return new IssuedAdminToken(ToInfo(stored), token);
    }

    public async Task<AdminTokenInfo[]> ListAsync() => [.. (await repository.ListTokensAsync()).Select(ToInfo)];

    public Task<bool> RevokeAsync(uint tokenId) => repository.DeleteTokenAsync(tokenId);

    private static string NewToken() => TokenPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    private static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static AdminTokenInfo ToInfo(AdminToken token) => new(token.TokenId, token.TokenName, token.CreatedTime, token.LastUsedTime);
}
