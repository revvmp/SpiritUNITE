using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Servers.Http;
using SpiritUnite.Servers.Lobby;

namespace SpiritUnite.Resource;

public sealed class ResourceArchiveCatalog(
    IOptions<ResourceArchiveOptions> options,
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<HttpServerOptions> httpServer,
    ILogger<ResourceArchiveCatalog> logger) : IHostedService
{
    private volatile FrozenDictionary<int, ResourceArchive> archives = FrozenDictionary<int, ResourceArchive>.Empty;

    public bool TryGet(int productId, [NotNullWhen(true)] out ResourceArchive? archive) =>
        archives.TryGetValue(productId, out archive);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var root = options.Value.ResolveDirectory();
        logger.LogInformation("Resource archive directory is {Directory}; put each archive at <Directory>/<productId>/<archive>.ifs", root);
        if (!System.IO.Directory.Exists(root))
        {
            logger.LogWarning("Resource archive directory {Directory} does not exist; no resource archive is served. Set ResourceArchive.Directory or create <Directory>/<productId>/<archive>.ifs", root);
            return;
        }

        var servedBase = options.Value.ServedBase(lobbyAddress.Value, httpServer.Value);
        var found = new Dictionary<int, ResourceArchive>();
        var productFolders = new DirectoryInfo(root).EnumerateDirectories()
            .Where(folder => folder.Name != ImageCdnFolder.FolderName)
            .OrderBy(folder => folder.Name, StringComparer.Ordinal);
        foreach (var productFolder in productFolders)
        {
            if (await LoadProductAsync(productFolder, servedBase, cancellationToken) is { } archive)
            {
                found[archive.ProductId] = archive;
            }
        }

        archives = found.ToFrozenDictionary();
        logger.LogInformation("Resource archive catalog holds {Count} products from {Directory}", found.Count, root);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<ResourceArchive?> LoadProductAsync(DirectoryInfo productFolder, Uri servedBase, CancellationToken cancellationToken)
    {
        if (!ResourceArchiveFileName.TryParseProductFolder(productFolder.Name, out var folderProductId))
        {
            logger.LogWarning("Resource archive folder {Folder} skipped: its name is not a positive product id", productFolder.FullName);
            return null;
        }

        var candidates = productFolder.GetFiles("*" + ResourceArchiveFileName.Extension)
            .Where(file => file.Extension.Equals(ResourceArchiveFileName.Extension, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (candidates.Length != 1)
        {
            logger.LogWarning("Resource archive for product {ProductId} skipped: {Folder} holds {Count} .ifs files, expected exactly one", folderProductId, productFolder.FullName, candidates.Length);
            return null;
        }

        var file = candidates[0];
        if (!ResourceArchiveFileName.TryParse(file.Name, out var productId, out var version))
        {
            logger.LogWarning("Resource archive {File} refused: the name does not match <gameid>_<productId>_<a.b.c.d>_<name>{Extension} with version parts 0-65535", file.FullName, ResourceArchiveFileName.Extension);
            return null;
        }

        if (productId != folderProductId)
        {
            logger.LogWarning("Resource archive {File} refused: its name says product {NameProductId} but it is in folder {FolderProductId}", file.FullName, productId, folderProductId);
            return null;
        }

        ResourceArchiveHash hash;
        try
        {
            hash = await LoadHashAsync(file, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Resource archive {File} refused: it could not be read ({Reason})", file.FullName, exception.Message);
            return null;
        }

        var (url, secureUrl) = ResourceArchiveOptions.ArchiveUrls(servedBase, productId, file.Name);
        logger.LogInformation("Resource archive for product {ProductId}: {File}, {Length} bytes, version {Version}, md5 {Md5}, served at {Url}", productId, file.Name, hash.Length, version, hash.Md5, url);
        return new ResourceArchive(productId, version, hash.Md5, url, secureUrl, file.FullName, hash.Length);
    }

    private async Task<ResourceArchiveHash> LoadHashAsync(FileInfo file, CancellationToken cancellationToken)
    {
        if (await ResourceArchiveHash.ReadSidecarAsync(file.FullName, cancellationToken) is { } cached && cached.Describes(file))
        {
            logger.LogInformation("Resource archive {File} md5 read from its sidecar", file.Name);
            return cached;
        }

        logger.LogInformation("Resource archive {File} md5 hashing started ({Length} bytes)", file.Name, file.Length);
        var elapsed = Stopwatch.StartNew();
        var hash = await ResourceArchiveHash.ComputeAsync(file, cancellationToken);
        logger.LogInformation("Resource archive {File} md5 hashing finished in {Elapsed}", file.Name, elapsed.Elapsed);
        try
        {
            await hash.WriteSidecarAsync(file.FullName, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Resource archive {File} md5 sidecar could not be written ({Reason}); it is hashed again on the next start", file.Name, exception.Message);
        }

        return hash;
    }
}
