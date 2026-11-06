using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpiritUNITE.Resource;

public sealed class ImageCdnFolder(IOptions<ResourceArchiveOptions> options, ILogger<ImageCdnFolder> logger) : IHostedService, IDisposable
{
    public const string FolderName = "images";
    public const string RoutePrefix = "/cdn";
    public const string sCDNHashMd5Name = "cdn_hash_md5_size.bytes";

    private const string ImageExtension = ".bytes";
    private const int MaximumPathLength = 512;

    private volatile PhysicalFileProvider? files;

    public IFileInfo? Find(string relativePath)
    {
        if (files is not { } provider
            || relativePath.Length is 0 or > MaximumPathLength
            || !relativePath.EndsWith(ImageExtension, StringComparison.Ordinal)
            || relativePath.Contains('\\'))
        {
            return null;
        }

        var file = provider.GetFileInfo(relativePath);
        return file is { Exists: true, IsDirectory: false, PhysicalPath: not null } ? file : null;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var root = Path.Combine(options.Value.ResolveDirectory(), FolderName);
        if (!Directory.Exists(root))
        {
            logger.LogWarning("Image CDN directory {Directory} does not exist; shop, banner and event images show the client's default picture. Put the image mirror in <ResourceArchive.Directory>/{FolderName}", root, FolderName);
            return Task.CompletedTask;
        }

        files = new PhysicalFileProvider(root);
        var index = new FileInfo(Path.Combine(root, sCDNHashMd5Name));
        if (!index.Exists)
        {
            logger.LogWarning("Image CDN directory {Directory} has no {IndexName}; the client cannot check its images. Copy the index into the image mirror", root, sCDNHashMd5Name);
            return Task.CompletedTask;
        }

        logger.LogInformation("Image CDN serves {Directory} at {RoutePrefix}, index {IndexName} {IndexBytes} bytes", root, RoutePrefix, sCDNHashMd5Name, index.Length);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => files?.Dispose();
}
