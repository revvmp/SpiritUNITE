using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Serilog.Events;
using SpiritUNITE.Auth;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.GiftCode;

namespace SpiritUNITE.Servers.Http;

public static class GiftCodeEndpoints
{
    public const string OfficialPagePath = "/act/cp/a20240410cdkey/index.html";
    public const string PagePath = "/giftcode";
    public const string GiftCodeField = "gift_code";
    public const string RoleIdField = "role_id";
    public const string ItopEncodeParamField = "itopencodeparam";
    public const string LoginField = "login";
    public const string PasswordField = "password";

    public static bool CarriesWebViewToken(LogEvent logEvent) =>
        logEvent.Properties.Values.Any(value => value.ToString().Contains(ItopEncodeParamField, StringComparison.OrdinalIgnoreCase));

    public static void Map(WebApplication app)
    {
        foreach (var path in new[] { OfficialPagePath, PagePath })
        {
            app.MapGet(path, ShowAsync);
            app.MapPost(path, SubmitAsync).DisableAntiforgery();
        }
    }

    private static async Task<IResult> ShowAsync(HttpRequest request, WebIdentity identity, RoleRepository roles)
    {
        var path = request.Path.Value ?? PagePath;
        if (ReadLink(request.Query) is not { } link)
        {
            return HasLinkFields(request.Query) ? InvalidLink() : HttpServer.Html(GiftCodePages.WithLogin(path, string.Empty, null, null));
        }

        if (await identity.FindWebViewAccountIdAsync(link.RoleId, link.ItopEncodeParam) is not { } uid)
        {
            return InvalidLink();
        }

        return await FindTrainerNameAsync(roles, uid) is { } trainerName
            ? HttpServer.Html(GiftCodePages.FromGame(path, link, trainerName, null, null))
            : HttpServer.Html(AccountPages.Page(GiftCodePages.Title, GiftCodePages.Title, GiftCodePages.NoTrainer, string.Empty));
    }

    private static async Task<IResult> SubmitAsync(
        HttpRequest request,
        WebIdentity identity,
        RoleRepository roles,
        GiftCodeManager giftCodes,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        if (await HttpServer.ReadSameSiteFormAsync(request, cancellationToken) is not { } form)
        {
            return InvalidLink();
        }

        var path = request.Path.Value ?? PagePath;
        var giftCode = HttpServer.SingleValue(form[GiftCodeField]);
        var logger = loggers.CreateLogger(typeof(GiftCodeEndpoints));
        if (ReadLink(form) is { } link)
        {
            if (await identity.FindWebViewAccountIdAsync(link.RoleId, link.ItopEncodeParam) is not { } linkedUid ||
                await FindTrainerNameAsync(roles, linkedUid) is not { } linkedName)
            {
                return InvalidLink();
            }

            var linkedResult = await giftCodes.RedeemAsync(linkedUid, giftCode);
            return HttpServer.Html(GiftCodePages.FromGame(path, link, linkedName, ErrorOf(linkedResult), NoticeOf(linkedResult)));
        }

        if (HasLinkFields(form))
        {
            return InvalidLink();
        }

        var login = HttpServer.SingleValue(form[LoginField]) ?? string.Empty;
        if (await identity.FindLoginAccountIdAsync(login, HttpServer.SingleValue(form[PasswordField]) ?? string.Empty) is not { } uid)
        {
            logger.LogInformation("Gift code page login refused from {Address}", request.HttpContext.Connection.RemoteIpAddress);
            return HttpServer.Html(GiftCodePages.WithLogin(path, login, GiftCodePages.LoginError, null));
        }

        if (await FindTrainerNameAsync(roles, uid) is null)
        {
            return HttpServer.Html(GiftCodePages.WithLogin(path, login, GiftCodePages.NoTrainer, null));
        }

        var result = await giftCodes.RedeemAsync(uid, giftCode);
        return HttpServer.Html(GiftCodePages.WithLogin(path, login, ErrorOf(result), NoticeOf(result)));
    }

    private static GiftCodeWebViewLink? ReadLink(IEnumerable<KeyValuePair<string, StringValues>> source)
    {
        var fields = source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return fields.TryGetValue(RoleIdField, out var roleId) && fields.TryGetValue(ItopEncodeParamField, out var itopEncodeParam) &&
            HttpServer.SingleValue(roleId) is { } singleRoleId && HttpServer.SingleValue(itopEncodeParam) is { } singleItopEncodeParam
            ? new GiftCodeWebViewLink(singleRoleId, singleItopEncodeParam)
            : null;
    }

    private static bool HasLinkFields(IEnumerable<KeyValuePair<string, StringValues>> source) =>
        source.Any(pair => pair.Key is RoleIdField or ItopEncodeParamField);

    private static async Task<string?> FindTrainerNameAsync(RoleRepository roles, ulong uid) =>
        (await roles.FindNamesAsync([uid])).FirstOrDefault()?.InGameUsrName;

    private static string? ErrorOf(GiftCodeRedeemResult result) =>
        result == GiftCodeRedeemResult.Redeemed ? null : GiftCodePages.RedeemError(result);

    private static string? NoticeOf(GiftCodeRedeemResult result) =>
        result == GiftCodeRedeemResult.Redeemed ? GiftCodePages.Exchanged : null;

    private static IResult InvalidLink() =>
        HttpServer.Html(GiftCodePages.InvalidLink(), StatusCodes.Status400BadRequest);
}
