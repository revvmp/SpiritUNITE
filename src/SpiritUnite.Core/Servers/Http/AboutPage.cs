using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace SpiritUnite.Servers.Http;

public static class AboutPage
{
    public const string Path = "/about";
    public const string RepositoryUrl = "https://github.com/revvmp/SpiritUNITE";

    private const string Title = "About";
    private const string Heading = "Have fun!";

    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    public static void Map(WebApplication app) => app.MapGet(Path, Show);

    private static IResult Show() => HttpServer.Html(AccountPages.Page(
        Title,
        Heading,
        null,
        $"""
        <p class="lead">This game runs on SpiritUNITE, a free and open-source Pokemon UNITE server.</p>
        <p class="lead"><a href="{Html.Encode(RepositoryUrl)}">SpiritUNITE on GitHub</a></p>
        """));
}
