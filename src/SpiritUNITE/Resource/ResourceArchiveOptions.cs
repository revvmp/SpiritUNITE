using SpiritUNITE.Servers.Http;
using SpiritUNITE.Servers.Lobby;

namespace SpiritUNITE.Resource;

public sealed class ResourceArchiveOptions
{
    public const string SectionName = "ResourceArchive";
    public const string RoutePrefix = "/resources";

    public string Directory { get; init; } = "resources";
    public string BaseUrl { get; init; } = string.Empty;

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(Directory)
        && (BaseUrl.Length == 0 || TryParseBaseUrl(BaseUrl, out _));

    public string ResolveDirectory() => Path.GetFullPath(Directory, System.IO.Directory.GetCurrentDirectory());

    public Uri ServedBase(LobbyAddressOptions lobbyAddress, HttpServerOptions httpServer) =>
        TryParseBaseUrl(BaseUrl, out var baseUrl)
            ? baseUrl
            : new UriBuilder(Uri.UriSchemeHttp, lobbyAddress.Host, httpServer.Port).Uri;

    public static (string Url, string SecureUrl) ArchiveUrls(Uri servedBase, int productId, string fileName)
    {
        var path = $"{servedBase.AbsolutePath.TrimEnd('/')}{RoutePrefix}/{productId}/{Uri.EscapeDataString(fileName)}";
        return (WithScheme(servedBase, Uri.UriSchemeHttp, path), WithScheme(servedBase, Uri.UriSchemeHttps, path));
    }

    private static string WithScheme(Uri servedBase, string scheme, string path) =>
        new UriBuilder(scheme, servedBase.Host, servedBase.Scheme == scheme ? servedBase.Port : -1, path).Uri.AbsoluteUri;

    private static bool TryParseBaseUrl(string value, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? baseUrl) =>
        Uri.TryCreate(value, UriKind.Absolute, out baseUrl)
        && (baseUrl.Scheme == Uri.UriSchemeHttp || baseUrl.Scheme == Uri.UriSchemeHttps)
        && baseUrl.Query.Length == 0
        && baseUrl.Fragment.Length == 0
        && baseUrl.UserInfo.Length == 0;
}
