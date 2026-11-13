using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SpiritUnite.Resource;
using SpiritUnite.Servers.Http;
using SpiritUnite.Servers.Lobby;

namespace SpiritUnite.Servers.Update;

public sealed record AppUpdatePackage(long Version, string FileName, string Url, string Md5, byte[] Content);

public sealed class AppUpdatePackages(
    ResourceArchiveCatalog catalog,
    IOptions<ResourceArchiveOptions> resourceArchive,
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<HttpServerOptions> httpServer)
{
    public const string RouteSegment = "apkupdate";
    public const string FileSuffix = "_apkupdate.json";

    private const long DEFAULT_APP_RES_VERSION = 0;
    private const int VersionPartBits = 16;
    private const long VersionPartMask = 0xFFFF;

    private static readonly IReadOnlyDictionary<long, int> PufferProductIdByDolphinProductId = new Dictionary<long, int>
    {
        [3215] = 1169,
        [3216] = 1170,
        [3217] = 1171,
        [3125] = 1117,
    };

    private static readonly byte[] StoreUpdateManifest = Encoding.UTF8.GetBytes(string.Join('\n',
        "{",
        "    \"AllChannelsFull\": {",
        "        \"md5\": \"\", ",
        "        \"name\": \"\", ",
        "        \"size\": 0, ",
        "        \"url\": \"\"",
        "    }, ",
        "    \"full\": {",
        "        \"completedmd5\": \"\", ",
        "        \"datacompletedmd5\": \"\", ",
        "        \"filesize\": 0, ",
        "        \"fullapkname\": \"\", ",
        "        \"toversion\": \"\", ",
        "        \"url\": \"\", ",
        "        \"versionInterval\": 5",
        "    }, ",
        "    \"predownload\": \"false\"",
        "}"));

    private static readonly string StoreUpdateManifestMd5 = Convert.ToHexStringLower(MD5.HashData(StoreUpdateManifest));

    public bool TryGetNewest(long dolphinProductId, [NotNullWhen(true)] out AppUpdatePackage? package)
    {
        package = null;
        if (!PufferProductIdByDolphinProductId.TryGetValue(dolphinProductId, out var pufferProductId)
            || !catalog.TryGet(pufferProductId, out var archive))
        {
            return false;
        }

        var version = (archive.Version & ~VersionPartMask) | DEFAULT_APP_RES_VERSION;
        var fileName = $"{dolphinProductId}_{VersionName(version)}{FileSuffix}";
        var servedBase = resourceArchive.Value.ServedBase(lobbyAddress.Value, httpServer.Value);
        var path = $"{servedBase.AbsolutePath.TrimEnd('/')}{ResourceArchiveOptions.RoutePrefix}/{RouteSegment}/{dolphinProductId}/{Uri.EscapeDataString(fileName)}";
        var url = new UriBuilder(servedBase.Scheme, servedBase.Host, servedBase.Port, path).Uri.AbsoluteUri;
        package = new AppUpdatePackage(version, fileName, url, StoreUpdateManifestMd5, StoreUpdateManifest);
        return true;
    }

    public static string VersionName(long version) => string.Join('.',
        Enumerable.Range(0, 4).Select(part => (version >> (VersionPartBits * (3 - part))) & VersionPartMask));
}
