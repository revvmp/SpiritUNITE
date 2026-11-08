using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.ActivationCode;
using SpiritUNITE.Auth;
using SpiritUNITE.Game.Admin;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Servers.Http.Admin;

public sealed record AdminBanRequest(int Days, string? Reason);

public sealed record AdminCreditRequest(int CreditPoint);

public sealed record AdminMailRequest(string? Subject, string? Content, AdminAttachment[]? Attachments, bool OnlineOnly);

public sealed record AdminTokenRequest(string? Name);

public sealed record AdminRenameRequest(string? Name);

public sealed record AdminActivationCodeRequest(string? ActivationCode, long ValidEndTime, int MaxUseCnt);

public sealed record AdminError(string Error);

public static class AdminEndpoints
{
    public const string PagePath = "/admin";
    public const string ContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; " +
        "form-action 'none'; base-uri 'none'; frame-ancestors 'none'";

    private const string ApiPath = "/admin/api";
    private const string BearerScheme = "Bearer ";
    private const string AdminItemKey = "SpiritUNITE.Admin";

    public static bool IsAdminPath(PathString path) => path.StartsWithSegments(PagePath, StringComparison.OrdinalIgnoreCase);

    public static void Map(WebApplication app)
    {
        AdminPage.Map(app);
        app.Lifetime.ApplicationStarted.Register(() => LogPanelAddresses(app));
        var api = app.MapGroup(ApiPath).AddEndpointFilter(AuthenticateAsync).DisableAntiforgery();
        api.MapGet("/me", (HttpContext context) => Results.Json(new { name = AdminOf(context).TokenName }));
        api.MapGet("/overview", async (AdminPlayers players) => Results.Json(await players.OverviewAsync()));
        api.MapGet("/online", async (AdminPlayers players) => Results.Json(await players.ListOnlineAsync()));
        api.MapGet("/players", ListPlayersAsync);
        api.MapGet("/players/{uid}", FindPlayerAsync);
        api.MapPost("/players/{uid}/mail", SendPlayerMailAsync);
        api.MapPost("/players/{uid}/kick", KickAsync);
        api.MapPost("/players/{uid}/ban", BanAsync);
        api.MapPost("/players/{uid}/unban", UnbanAsync);
        api.MapPost("/players/{uid}/credit", SetCreditAsync);
        api.MapPost("/players/{uid}/clear-battle-ban", ClearBattleBanAsync);
        api.MapPost("/players/{uid}/rename", RenameAsync);
        api.MapPost("/players/{uid}/give-all/{category}", GiveAllAsync);
        api.MapGet("/server", async (AdminServerState state) => Results.Json(await state.SnapshotAsync()));
        api.MapGet("/reports", async (ulong? before, AdminReports reports) => Results.Json(await reports.ListAsync(before)));
        api.MapGet("/squads", async (AdminSquads squads) => Results.Json(await squads.ListAsync()));
        api.MapGet("/squads/{guildID}", FindSquadAsync);
        api.MapPost("/squads/{guildID}/members/{uid}/remove", RemoveSquadMemberAsync);
        api.MapPost("/mail", BroadcastAsync);
        api.MapGet("/catalog", (AdminCatalog catalog) => Results.Json(catalog.Data));
        api.MapGet("/settings", async (AdminSettings settings) => Results.Json(await settings.ListAsync()));
        api.MapPut("/settings/{name}", SaveSettingsAsync);
        api.MapGet("/activation-codes", async (ActivationCodeManager codes) => Results.Json(await codes.ListHostCodeEntriesAsync()));
        api.MapPost("/activation-codes", AddActivationCodeAsync);
        api.MapDelete("/activation-codes/{activationCode}", RemoveActivationCodeAsync);
        api.MapGet("/tokens", async (AdminTokenManager tokens) => Results.Json(await tokens.ListAsync()));
        api.MapPost("/tokens", CreateTokenAsync);
        api.MapDelete("/tokens/{tokenId}", RevokeTokenAsync);
        api.MapGet("/audit", async (ulong? before, AdminAuditLog audit) => Results.Json(await audit.ListAsync(before)));
        AdminGiftCodeEndpoints.Map(api);
    }

    private static void LogPanelAddresses(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminEndpoints));
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        foreach (var address in addresses)
        {
            logger.LogInformation("Admin panel: {Url}", address.TrimEnd('/') + PagePath);
        }

        logger.LogWarning("Admin token for this run: {Token}", app.Services.GetRequiredService<AdminTokenManager>().StartupToken);
    }

    private static async ValueTask<object?> AuthenticateAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var context = invocation.HttpContext;
        var header = context.Request.Headers.Authorization.ToString();
        var tokens = context.RequestServices.GetRequiredService<AdminTokenManager>();
        if (!header.StartsWith(BearerScheme, StringComparison.Ordinal) ||
            await tokens.AuthenticateAsync(header[BearerScheme.Length..].Trim()) is not { } admin)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AdminEndpoints))
                .LogInformation("Admin request refused from {Address}", context.Connection.RemoteIpAddress);
            return Results.Json(new AdminError("Invalid token"), statusCode: StatusCodes.Status401Unauthorized);
        }

        context.Items[AdminItemKey] = admin;
        return await next(invocation);
    }

    internal static AdminIdentity AdminOf(HttpContext context) => (AdminIdentity)context.Items[AdminItemKey]!;

    private static async Task<IResult> ListPlayersAsync(string? q, int? page, AdminPlayers players)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Results.Json(await players.ListAsync(page ?? 0));
        }

        var found = await players.SearchAsync(q);
        return Results.Json(new AdminPlayerPage(found.Length, AdminPlayers.PageSize, found));
    }

    private static async Task<IResult> FindPlayerAsync(ulong uid, AdminPlayers players) =>
        await players.FindAsync(uid) is { } detail ? Results.Json(detail) : NotFound();

    private static async Task<IResult> SendPlayerMailAsync(
        ulong uid,
        AdminMailRequest request,
        HttpContext context,
        AdminPlayers players,
        AdminMail mail,
        AdminAuditLog audit)
    {
        if (await players.FindAsync(uid) is not { Name.Length: > 0 })
        {
            return Refuse("This account has no trainer yet");
        }

        var draft = ToDraft(request);
        var build = mail.Build(draft);
        if (build.Mail is not { } systemMail)
        {
            return Refuse(build.Error ?? "Mail cannot be sent");
        }

        await mail.SendAsync(uid, systemMail);
        await audit.RecordAsync(AdminOf(context), "mail", UidTarget(uid), Describe(draft));
        return Results.Json(new { sent = 1 });
    }

    private static async Task<IResult> BroadcastAsync(HttpContext context, AdminMailRequest request, AdminMail mail, AdminAuditLog audit)
    {
        var draft = ToDraft(request);
        var build = mail.Build(draft);
        if (build.Mail is not { } systemMail)
        {
            return Refuse(build.Error ?? "Mail cannot be sent");
        }

        var sent = await mail.BroadcastAsync(systemMail, request.OnlineOnly);
        await audit.RecordAsync(AdminOf(context), "broadcast", request.OnlineOnly ? "online" : "all", $"{sent} players. {Describe(draft)}");
        return Results.Json(new { sent });
    }

    private static async Task<IResult> KickAsync(ulong uid, HttpContext context, AdminPlayers players, AdminAuditLog audit)
    {
        if (!await players.KickAsync(uid))
        {
            return Refuse("Player is offline");
        }

        await audit.RecordAsync(AdminOf(context), "kick", UidTarget(uid), string.Empty);
        return Results.Json(new { kicked = true });
    }

    private static async Task<IResult> BanAsync(ulong uid, AdminBanRequest request, HttpContext context, AdminPlayers players, AdminAuditLog audit)
    {
        var reason = request.Reason ?? string.Empty;
        if (!AdminPlayers.IsBanDays(request.Days) || reason.Length > AccountManager.MaximumBanReasonLength)
        {
            return Refuse("Invalid ban");
        }

        if (!await players.BanAsync(uid, request.Days, reason))
        {
            return NotFound();
        }

        await audit.RecordAsync(AdminOf(context), "ban", UidTarget(uid), request.Days == 0 ? $"Permanent. {reason}" : $"{request.Days} days. {reason}");
        return Results.Json(await players.FindAsync(uid));
    }

    private static async Task<IResult> UnbanAsync(ulong uid, HttpContext context, AdminPlayers players, AdminAuditLog audit)
    {
        if (!await players.UnbanAsync(uid))
        {
            return Refuse("Player is not banned");
        }

        await audit.RecordAsync(AdminOf(context), "unban", UidTarget(uid), string.Empty);
        return Results.Json(await players.FindAsync(uid));
    }

    private static async Task<IResult> SetCreditAsync(
        ulong uid,
        AdminCreditRequest request,
        HttpContext context,
        AdminPlayers players,
        AdminAuditLog audit)
    {
        if (await players.FindAsync(uid) is not { Name.Length: > 0 } before)
        {
            return NotFound();
        }

        if (!await players.SetCreditPointAsync(uid, request.CreditPoint))
        {
            return Refuse("Credit is out of range");
        }

        await audit.RecordAsync(AdminOf(context), "credit", UidTarget(uid), $"{before.CreditPoint} to {request.CreditPoint}");
        return Results.Json(await players.FindAsync(uid));
    }

    private static async Task<IResult> ClearBattleBanAsync(ulong uid, HttpContext context, AdminPlayers players, AdminAuditLog audit)
    {
        if (!await players.ClearBattleBanAsync(uid))
        {
            return Refuse("No match ban to clear");
        }

        await audit.RecordAsync(AdminOf(context), "clear match ban", UidTarget(uid), string.Empty);
        return Results.Json(await players.FindAsync(uid));
    }

    private static async Task<IResult> RenameAsync(
        ulong uid,
        AdminRenameRequest request,
        HttpContext context,
        AdminPlayers players,
        AdminAuditLog audit)
    {
        if (await players.FindAsync(uid) is not { } before)
        {
            return NotFound();
        }

        if (await players.RenameAsync(uid, request.Name ?? string.Empty) is { } error)
        {
            return Refuse(error);
        }

        var after = await players.FindAsync(uid);
        await audit.RecordAsync(AdminOf(context), "rename", UidTarget(uid), $"{before.Name} to {after?.Name}");
        return Results.Json(after);
    }

    private static async Task<IResult> GiveAllAsync(ulong uid, string category, HttpContext context, AdminGiveAll giveAll, AdminAuditLog audit)
    {
        if (!AdminGiveAll.IsCategory(category))
        {
            return Refuse("Unknown category");
        }

        if (await giveAll.GiveAsync(uid, category) is not { } result)
        {
            return Refuse("This account has no trainer yet, or the grant failed");
        }

        await audit.RecordAsync(AdminOf(context), "give all", UidTarget(uid), $"{category}: {result.Granted} given, {result.Available - result.Granted} already owned");
        return Results.Json(result);
    }

    private static async Task<IResult> FindSquadAsync(ulong guildID, AdminSquads squads) =>
        await squads.FindAsync(guildID) is { } squad ? Results.Json(squad) : NotFound();

    private static async Task<IResult> RemoveSquadMemberAsync(ulong guildID, ulong uid, HttpContext context, AdminSquads squads, AdminAuditLog audit)
    {
        if (await squads.RemoveMemberAsync(guildID, uid) != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return Refuse("This trainer cannot be removed");
        }

        await audit.RecordAsync(AdminOf(context), "squad remove", guildID.ToString(CultureInfo.InvariantCulture), UidTarget(uid));
        return await FindSquadAsync(guildID, squads);
    }

    private static async Task<IResult> SaveSettingsAsync(
        string name,
        JsonObject values,
        HttpContext context,
        AdminSettings settings,
        AdminAuditLog audit)
    {
        var before = (await settings.ListAsync()).FirstOrDefault(section => section.Name == name);
        var save = await settings.SaveAsync(name, values);
        if (save.Section is not { } section)
        {
            return Refuse(save.Error ?? "Settings cannot be saved");
        }

        await audit.RecordAsync(AdminOf(context), "settings", name, DescribeChanges(before?.Values ?? [], section.Values));
        return Results.Json(section);
    }

    private static async Task<IResult> AddActivationCodeAsync(AdminActivationCodeRequest request, HttpContext context, ActivationCodeManager codes, AdminAuditLog audit)
    {
        var code = new HostActivationCode
        {
            ActivationCode = request.ActivationCode?.Trim() ?? string.Empty,
            ValidEndTime = request.ValidEndTime,
            MaxUseCnt = request.MaxUseCnt,
        };
        if (await codes.AddHostCodeAsync(code) is { } error)
        {
            return Refuse(error);
        }

        await audit.RecordAsync(AdminOf(context), "activation code add", code.ActivationCode, $"valid until {code.ValidEndTime}, max uses {code.MaxUseCnt}");
        return Results.Json(new HostActivationCodeEntry(code.ActivationCode, code.ValidEndTime, code.MaxUseCnt, 0));
    }

    private static async Task<IResult> RemoveActivationCodeAsync(string activationCode, HttpContext context, ActivationCodeManager codes, AdminAuditLog audit)
    {
        if (!await codes.RemoveHostCodeAsync(activationCode))
        {
            return NotFound();
        }

        await audit.RecordAsync(AdminOf(context), "activation code remove", activationCode, string.Empty);
        return Results.Json(new { removed = true });
    }

    private static async Task<IResult> CreateTokenAsync(AdminTokenRequest request, HttpContext context, AdminTokenManager tokens, AdminAuditLog audit)
    {
        if (await tokens.CreateAsync(request.Name) is not { } issued)
        {
            return Refuse($"Names are 1 to {AdminTokenManager.MaximumTokenNameLength} characters");
        }

        await audit.RecordAsync(AdminOf(context), "token create", issued.Info.TokenName, string.Empty);
        return Results.Json(issued);
    }

    private static async Task<IResult> RevokeTokenAsync(uint tokenId, HttpContext context, AdminTokenManager tokens, AdminAuditLog audit)
    {
        var admin = AdminOf(context);
        if (tokenId == admin.TokenId)
        {
            return Refuse("You cannot revoke the token you are using");
        }

        var revoked = (await tokens.ListAsync()).FirstOrDefault(token => token.TokenId == tokenId);
        if (revoked is null || !await tokens.RevokeAsync(tokenId))
        {
            return NotFound();
        }

        await audit.RecordAsync(admin, "token revoke", revoked.TokenName, string.Empty);
        return Results.Json(new { revoked = true });
    }

    private static AdminMailDraft ToDraft(AdminMailRequest request) =>
        new(request.Subject ?? string.Empty, request.Content ?? string.Empty, request.Attachments ?? []);

    private static string Describe(AdminMailDraft draft) =>
        draft.Attachments.Count == 0
            ? draft.Subject
            : $"{draft.Subject}: {string.Join(", ", draft.Attachments.Select(attachment => $"{attachment.Kind} {attachment.Id} x{attachment.Count}"))}";

    private static string DescribeChanges(JsonObject before, JsonObject after) =>
        string.Join(
            ". ",
            after
                .Where(property => !JsonNode.DeepEquals(before[property.Key], property.Value))
                .Select(property => $"{property.Key}: {AuditValue(before[property.Key])} to {AuditValue(property.Value)}"));

    private static string AuditValue(JsonNode? value) => WithoutNulls(value)?.ToJsonString() ?? "empty";

    private static JsonNode? WithoutNulls(JsonNode? value) => value switch
    {
        JsonObject properties => new JsonObject(properties
            .Where(property => property.Value is not null)
            .Select(property => KeyValuePair.Create(property.Key, WithoutNulls(property.Value)))),
        JsonArray items => new JsonArray([.. items.Select(WithoutNulls)]),
        _ => value?.DeepClone(),
    };

    private static string UidTarget(ulong uid) => uid.ToString(CultureInfo.InvariantCulture);

    internal static IResult Refuse(string error) => Results.Json(new AdminError(error), statusCode: StatusCodes.Status400BadRequest);

    internal static IResult NotFound() => Results.Json(new AdminError("Not found"), statusCode: StatusCodes.Status404NotFound);
}
