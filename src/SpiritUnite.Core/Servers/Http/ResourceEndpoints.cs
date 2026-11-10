using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SpiritUnite.Resource;

namespace SpiritUnite.Servers.Http;

public static class ResourceEndpoints
{
    private const string ArchivePath = ResourceArchiveOptions.RoutePrefix + "/{productId:int}/{file}";
    private const string ArchiveContentType = "application/octet-stream";
    private const string FullFile = "full";
    private const string UnreadableRange = "unreadable range";

    public static void Map(WebApplication app)
    {
        app.MapMethods(ArchivePath, [HttpMethods.Get, HttpMethods.Head], ServeArchive);
    }

    private static IResult ServeArchive(int productId, string file, HttpRequest request, ResourceArchiveCatalog catalog, ILoggerFactory loggers)
    {
        var logger = loggers.CreateLogger(typeof(ResourceEndpoints));
        var range = DescribeRange(request);
        var clientAddress = request.HttpContext.Connection.RemoteIpAddress;
        if (!catalog.TryGet(productId, out var archive) || !string.Equals(archive.FileName, file, StringComparison.Ordinal))
        {
            logger.LogInformation("Resource archive request refused: product {ProductId}, file {File}, {Range}, from {Address}", productId, Uri.EscapeDataString(file), range, clientAddress);
            return Results.NotFound();
        }

        logger.LogInformation("Resource archive {Method} product {ProductId}, file {File}, {Range}, from {Address}", request.Method, productId, file, range, clientAddress);
        return Results.Stream(
            archive.OpenRead(),
            ArchiveContentType,
            entityTag: new EntityTagHeaderValue($"\"{archive.Md5}\""),
            enableRangeProcessing: true);
    }

    private static string DescribeRange(HttpRequest request)
    {
        if (request.Headers.Range.Count == 0)
        {
            return FullFile;
        }

        return RangeHeaderValue.TryParse(request.Headers.Range.ToString(), out var range) ? range.ToString() : UnreadableRange;
    }
}
