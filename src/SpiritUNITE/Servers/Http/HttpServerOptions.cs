using System.Net;
using System.Net.Sockets;

namespace SpiritUNITE.Servers.Http;

public sealed class HttpServerOptions
{
    public const string SectionName = "HttpServer";
    public const string PokemonChannelRedirectUri = "https://cdn.itopsdk.com/v5/tpci/prod/loginSuccess.html";
    public const string NintendoChannelRedirectUri = "https://cdn.itopsdk.com/v5/release/nintendo/loginSuccess.html";
    public const string CodeChallengeOptionalRedirectUri = NintendoChannelRedirectUri;

    public string BindAddress { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 10081;

    public string[] AllowedRedirectUris { get; set; } = [];

    public string[] TrustedProxies { get; set; } = [];

    public IReadOnlyCollection<string> RedirectUris =>
        AllowedRedirectUris.Length > 0 ? AllowedRedirectUris : [PokemonChannelRedirectUri, NintendoChannelRedirectUri];

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
        AllowedRedirectUris.All(uri => Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps) &&
        TrustedProxies.All(proxy => TryParseProxyNetwork(proxy, out _));
}
