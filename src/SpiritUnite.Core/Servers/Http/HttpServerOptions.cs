using System.Net;
using System.Net.Sockets;

namespace SpiritUnite.Servers.Http;

public sealed class HttpServerOptions
{
    public const string SectionName = "HttpServer";
    public const string DefaultPokemonRedirectUri = "https://spiritunite.invalid/pokemon/loginSuccess";
    public const string DefaultNintendoRedirectUri = "https://spiritunite.invalid/nintendo/loginSuccess";

    public string BindAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 10081;

    public string PokemonRedirectUri { get; set; } = DefaultPokemonRedirectUri;

    public string NintendoRedirectUri { get; set; } = DefaultNintendoRedirectUri;

    public string[] TrustedProxies { get; set; } = [];

    public string PublicOrigin { get; set; } = string.Empty;

    public IReadOnlyCollection<string> RedirectUris => [PokemonRedirectUri, NintendoRedirectUri];

    public string CodeChallengeOptionalRedirectUri => NintendoRedirectUri;

    public static bool TryParseProxyNetwork(string proxy, out IPNetwork network)
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            network = new IPNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128);
            return true;
        }

        return IPNetwork.TryParse(proxy, out network);
    }

    public bool IsValid() =>
        IPAddress.TryParse(BindAddress, out _) &&
        Port is > IPEndPoint.MinPort and <= IPEndPoint.MaxPort &&
        RedirectUris.All(IsHttpsUri) &&
        TrustedProxies.All(proxy => TryParseProxyNetwork(proxy, out _)) &&
        (PublicOrigin.Length == 0 || TryParseOrigin(PublicOrigin, out _));

    public Uri WebOrigin(string lobbyHost)
    {
        if (TryParseOrigin(PublicOrigin, out var configured))
        {
            return configured;
        }

        return IPAddress.TryParse(lobbyHost, out _)
            ? new UriBuilder(Uri.UriSchemeHttp, lobbyHost, Port).Uri
            : new UriBuilder(Uri.UriSchemeHttps, lobbyHost).Uri;
    }

    private static bool TryParseOrigin(string value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? origin) =>
        Uri.TryCreate(value, UriKind.Absolute, out origin)
        && (origin.Scheme == Uri.UriSchemeHttp || origin.Scheme == Uri.UriSchemeHttps)
        && origin.AbsolutePath == "/"
        && origin.Query.Length == 0
        && origin.Fragment.Length == 0
        && origin.UserInfo.Length == 0;

    private static bool IsHttpsUri(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps;
}
