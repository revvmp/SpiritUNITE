using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SpiritUnite.Resource;

namespace SpiritUnite.Servers.Http;

public static class GalleryEndpoints
{
    public const string PagePath = GalleryFolder.RoutePrefix + "/" + GalleryFolder.IndexPage;
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self'; " +
        "form-action 'none'; base-uri 'none'; frame-ancestors 'none'";

    private const string FilePath = GalleryFolder.RoutePrefix + "/{**path}";

    public static bool IsGalleryPath(PathString path) => path.StartsWithSegments(GalleryFolder.RoutePrefix, StringComparison.OrdinalIgnoreCase);

    public static void Map(WebApplication app)
    {
        app.MapMethods(FilePath, [HttpMethods.Get, HttpMethods.Head], ServeFile);
    }

    private static IResult ServeFile(string? path, HttpRequest request, GalleryFolder folder, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(GalleryEndpoints));
        var clientAddress = request.HttpContext.Connection.RemoteIpAddress;
        if (folder.Find(path ?? string.Empty) is not ({ PhysicalPath: { } physicalPath } file, var contentType))
        {
            logger.LogInformation("Gallery has no {Path}, from {Address}", LoggablePath(path), clientAddress);
            return Results.NotFound();
        }

        logger.LogDebug("Gallery {Method} {Path}, from {Address}", request.Method, LoggablePath(path), clientAddress);
        return Results.File(physicalPath, contentType, lastModified: file.LastModified, enableRangeProcessing: true);
    }

    private static string LoggablePath(string? path) =>
        Uri.EscapeDataString(path ?? string.Empty).Replace("%2F", "/", StringComparison.Ordinal);
}
