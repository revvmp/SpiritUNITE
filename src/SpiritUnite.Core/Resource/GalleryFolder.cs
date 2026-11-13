using System.Collections.Frozen;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpiritUnite.Resource;

public sealed class GalleryFolder(IOptions<ResourceArchiveOptions> options, ILogger<GalleryFolder> logger) : IHostedService, IDisposable
{
    public const string FolderName = "gallery";
    public const string RoutePrefix = "/gallery";
    public const string IndexPage = "index.html";

    private const int MaximumPathLength = 256;

    private static readonly FrozenDictionary<string, string> ContentTypeByExtension = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private volatile PhysicalFileProvider? files;

    public (IFileInfo File, string ContentType)? Find(string relativePath)
    {
        if (files is not { } provider
            || relativePath.Length is 0 or > MaximumPathLength
            || relativePath.Contains('\\')
            || !ContentTypeByExtension.TryGetValue(Path.GetExtension(relativePath), out var contentType))
        {
            return null;
        }

        var file = provider.GetFileInfo(relativePath);
        return file is { Exists: true, IsDirectory: false, PhysicalPath: not null } ? (file, contentType) : null;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var root = Path.Combine(options.Value.ResolveDirectory(), FolderName);
        if (!File.Exists(Path.Combine(root, IndexPage)))
        {
            logger.LogWarning("Gallery directory {Directory} has no {IndexPage}; Menu > Gallery shows a missing page. Download it with tools/fetch_resources.py gallery", root, IndexPage);
            return Task.CompletedTask;
        }

        files = new PhysicalFileProvider(root);
        logger.LogInformation("Gallery serves {Directory} at {RoutePrefix}", root, RoutePrefix);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => files?.Dispose();
}
