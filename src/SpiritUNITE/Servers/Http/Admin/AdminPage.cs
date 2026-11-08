using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SpiritUNITE.Servers.Http.Admin;

public static class AdminPage
{
    private const string ResourcePrefix = "AdminPage.";

    private static readonly (string Path, string Resource, string ContentType)[] Files =
    [
        (AdminEndpoints.PagePath, "index.html", "text/html; charset=utf-8"),
        (AdminEndpoints.PagePath + "/admin.css", "admin.css", "text/css; charset=utf-8"),
        (AdminEndpoints.PagePath + "/admin.js", "admin.js", "text/javascript; charset=utf-8"),
        (AdminEndpoints.PagePath + "/icon.svg", "icon.svg", "image/svg+xml"),
    ];

    public static void Map(WebApplication app)
    {
        foreach (var (path, resource, contentType) in Files)
        {
            var content = Load(resource);
            app.MapGet(path, () => Results.Bytes(content, contentType));
        }
    }

    private static byte[] Load(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + resource)
            ?? throw new InvalidOperationException($"Admin page file {resource} is not embedded");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
