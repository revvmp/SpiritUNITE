using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Resource;

namespace SpiritUNITE.Servers.Http;

public static class ImageCdnEndpoints
{
    private const string ImagePath = ImageCdnFolder.RoutePrefix + "/{**path}";
    private const string ImageContentType = "application/octet-stream";

    public static void Map(WebApplication app)
    {
        app.MapMethods(ImagePath, [HttpMethods.Get, HttpMethods.Head], ServeImage);
    }

    private static IResult ServeImage(string? path, HttpRequest request, ImageCdnFolder folder, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(ImageCdnEndpoints));
        var clientAddress = request.HttpContext.Connection.RemoteIpAddress;
        if (folder.Find(path ?? string.Empty) is not { PhysicalPath: { } physicalPath } file)
        {
            logger.LogInformation("Image CDN has no {Path}, from {Address}", LoggablePath(path), clientAddress);
            return Results.NotFound();
        }

        logger.LogDebug("Image CDN {Method} {Path}, from {Address}", request.Method, path, clientAddress);
        return Results.File(physicalPath, ImageContentType, lastModified: file.LastModified, enableRangeProcessing: true);
    }

    private static string LoggablePath(string? path) =>
        Uri.EscapeDataString(path ?? string.Empty).Replace("%2F", "/", StringComparison.Ordinal);
}
