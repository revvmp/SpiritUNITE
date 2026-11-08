using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SpiritUnite.Auth;

public sealed record AuthorizationRequest(string RedirectUri, string? State, string? Scope, string? CodeChallenge)
{
    public const string RedirectUriParameter = "redirect_uri";
    public const string StateParameter = "state";
    public const string ScopeParameter = "scope";
    public const string ResponseTypeParameter = "response_type";
    public const string CodeChallengeParameter = "code_challenge";
    public const string CodeChallengeMethodParameter = "code_challenge_method";
    public const string CodeParameter = "code";

    public const string CodeResponseType = "code";
    public const string Sha256ChallengeMethod = "S256";
    public const int MinimumCodeVerifierLength = 43;
    public const int MaximumCodeVerifierLength = 128;

    public static readonly string[] ParameterNames =
    [
        RedirectUriParameter,
        StateParameter,
        ScopeParameter,
        ResponseTypeParameter,
        CodeChallengeParameter,
        CodeChallengeMethodParameter,
    ];

    private const int MaximumParameterLength = 2048;
    private const int Sha256ChallengeLength = 43;

    public static AuthorizationRequest? TryCreate(
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyCollection<string> allowedRedirectUris,
        string codeChallengeOptionalRedirectUri)
    {
        if (parameters.Values.Any(value => value.Length > MaximumParameterLength))
        {
            return null;
        }

        if (!parameters.TryGetValue(RedirectUriParameter, out var redirectUri) || !allowedRedirectUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            return null;
        }

        if (parameters.TryGetValue(ResponseTypeParameter, out var responseType) && responseType != CodeResponseType)
        {
            return null;
        }

        var codeChallenge = parameters.GetValueOrDefault(CodeChallengeParameter);
        var codeChallengeMethod = parameters.GetValueOrDefault(CodeChallengeMethodParameter);
        var codeChallengeAccepted = codeChallenge is null && codeChallengeMethod is null
            ? string.Equals(redirectUri, codeChallengeOptionalRedirectUri, StringComparison.Ordinal)
            : codeChallengeMethod == Sha256ChallengeMethod && IsSha256CodeChallenge(codeChallenge);
        if (!codeChallengeAccepted)
        {
            return null;
        }

        return new AuthorizationRequest(
            redirectUri,
            parameters.GetValueOrDefault(StateParameter),
            parameters.GetValueOrDefault(ScopeParameter),
            codeChallenge);
    }

    public bool AcceptsVerifier(string? codeVerifier)
    {
        if (CodeChallenge is null)
        {
            return codeVerifier is null;
        }

        if (codeVerifier is null || !IsValidCodeVerifier(codeVerifier))
        {
            return false;
        }

        var expected = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(CodeChallenge));
    }

    public IEnumerable<KeyValuePair<string, string?>> ToParameters()
    {
        yield return new(RedirectUriParameter, RedirectUri);
        yield return new(ResponseTypeParameter, CodeResponseType);
        if (State is not null)
        {
            yield return new(StateParameter, State);
        }

        if (Scope is not null)
        {
            yield return new(ScopeParameter, Scope);
        }

        if (CodeChallenge is not null)
        {
            yield return new(CodeChallengeParameter, CodeChallenge);
            yield return new(CodeChallengeMethodParameter, Sha256ChallengeMethod);
        }
    }

    private static bool IsValidCodeVerifier(string codeVerifier) =>
        codeVerifier.Length is >= MinimumCodeVerifierLength and <= MaximumCodeVerifierLength &&
        codeVerifier.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~');

    private static bool IsSha256CodeChallenge(string? codeChallenge) =>
        codeChallenge is { Length: Sha256ChallengeLength } && codeChallenge.All(IsBase64UrlCharacter);

    private static bool IsBase64UrlCharacter(char character) => char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
