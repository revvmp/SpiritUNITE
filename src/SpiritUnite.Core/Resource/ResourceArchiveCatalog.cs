using System.Collections.Frozen;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Resource.Ifs;
using SpiritUnite.Servers.Http;
using SpiritUnite.Servers.Lobby;

namespace SpiritUnite.Resource;

public sealed class ResourceArchiveCatalog(
    IOptions<ResourceArchiveOptions> options,
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<HttpServerOptions> httpServer,
    ILogger<ResourceArchiveCatalog> logger) : IHostedService
{
    private const int HashBufferBytes = 4 * 1024 * 1024;

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
        var tablePatches = ClientTablePatches.For(httpServer.Value.WebOrigin(lobbyAddress.Value.Host));
        var found = new Dictionary<int, ResourceArchive>();
        var productFolders = new DirectoryInfo(root).EnumerateDirectories()
            .Where(folder => folder.Name is not (ImageCdnFolder.FolderName or GalleryFolder.FolderName))
            .OrderBy(folder => folder.Name, StringComparer.Ordinal);
        foreach (var productFolder in productFolders)
        {
            if (await LoadProductAsync(productFolder, servedBase, tablePatches, cancellationToken) is { } archive)
            {
                found[archive.ProductId] = archive;
            }
        }

        archives = found.ToFrozenDictionary();
        logger.LogInformation("Resource archive catalog holds {Count} products from {Directory}", found.Count, root);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<ResourceArchive?> LoadProductAsync(DirectoryInfo productFolder, Uri servedBase, IReadOnlyList<DatabinStringPatch> tablePatches, CancellationToken cancellationToken)
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
        if (!ResourceArchiveFileName.TryParse(file.Name, out var productId, out _))
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

        return await PatchAsync(file, hash, productId, servedBase, tablePatches, cancellationToken);
    }

    private async Task<ResourceArchive?> PatchAsync(
        FileInfo file,
        ResourceArchiveHash originalHash,
        int productId,
        Uri servedBase,
        IReadOnlyList<DatabinStringPatch> tablePatches,
        CancellationToken cancellationToken)
    {
        try
        {
            var state = await ResourceArchivePatchState.ReadLatestAsync(file.FullName, productId, cancellationToken);
            using var archive = new IfsArchive(file.FullName);
            var prepared = IfsPatcher.Prepare(archive, tablePatches);
            if (VersionOf(file.Name) != VersionOf(ResourceArchiveFileName.WithVersion(file.Name, prepared.BundleVersionName)))
            {
                throw new InvalidDataException("The archive filename version does not match its BundleVersionName");
            }

            var generation = state?.OriginalMd5 == originalHash.Md5 ? state.Generation : 1;
            var (layout, digest, versionName) = Assemble(archive, prepared, generation);
            var unchanged = state is not null && state.Describes(originalHash.Md5, digest) && state.Length == layout.Length;
            var servedName = ResourceArchiveFileName.WithVersion(file.Name, versionName);
            var version = VersionOf(servedName);
            if (state is not null)
            {
                var candidateVersion = unchecked((ulong)version);
                if (unchanged && candidateVersion != state.Version)
                {
                    throw new InvalidDataException("The saved patch version does not match the archive");
                }

                if (!unchanged && candidateVersion <= state.Version)
                {
                    if (candidateVersion >> 16 != state.Version >> 16 || (state.Version & ushort.MaxValue) == ushort.MaxValue)
                    {
                        throw new InvalidDataException("The archive cannot advance past the previously served version without changing its release version");
                    }

                    generation = checked(generation + (int)(state.Version - candidateVersion) + 1);
                    (layout, digest, versionName) = Assemble(archive, prepared, generation);
                    servedName = ResourceArchiveFileName.WithVersion(file.Name, versionName);
                }
            }

            version = VersionOf(servedName);

            string md5;
            if (unchanged)
            {
                md5 = state!.Md5;
            }
            else
            {
                logger.LogInformation("Resource archive {File} patched to version {Version}; md5 hashing started ({Length} bytes)", file.Name, versionName, layout.Length);
                var elapsed = Stopwatch.StartNew();
                md5 = await HashAsync(layout, cancellationToken);
                logger.LogInformation("Resource archive {File} patched md5 hashing finished in {Elapsed}", file.Name, elapsed.Elapsed);
            }

            await new ResourceArchivePatchState(originalHash.Md5, digest, generation, md5, layout.Length, unchecked((ulong)version)).WriteAsync(file.FullName, cancellationToken);

            var (url, secureUrl) = ResourceArchiveOptions.ArchiveUrls(servedBase, productId, servedName);
            logger.LogInformation(
                "Resource archive for product {ProductId}: {File} patched as {ServedName} (generation {Generation}, {Tables} client table values), {Length} bytes, md5 {Md5}, served at {Url}",
                productId, file.Name, servedName, generation, tablePatches.Count, layout.Length, md5, url);
            return new ResourceArchive(productId, version, md5, url, secureUrl, servedName, layout.Length, layout.OpenRead);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or KeyNotFoundException)
        {
            logger.LogWarning("Resource archive {File} refused: it could not be safely patched ({Reason}); no archive is served for product {ProductId}", file.Name, exception.Message, productId);
            return null;
        }
    }

    private static (PatchedIfsLayout Layout, string Digest, string VersionName) Assemble(IfsArchive archive, PreparedIfsPatch prepared, int generation)
    {
        var versionName = IfsPatcher.PatchedVersionName(prepared.BundleVersionName, generation);
        var layout = IfsPatcher.Assemble(archive, prepared, versionName);
        return (layout, layout.Digest(), versionName);
    }

    private static long VersionOf(string fileName) => ResourceArchiveFileName.TryParse(fileName, out _, out var version)
        ? version
        : throw new InvalidDataException($"Patched archive name {fileName} does not parse");

    private static async Task<string> HashAsync(PatchedIfsLayout layout, CancellationToken cancellationToken)
    {
        await using var stream = layout.OpenRead();
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var buffer = new byte[HashBufferBytes];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            md5.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(md5.GetHashAndReset());
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
