using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SpiritUNITE.Auth;
using SpiritUNITE.Game.Admin;
using SpiritUNITE.Game.GiftCode;

namespace SpiritUNITE.Servers.Http.Admin;

public sealed record AdminGiftCodeRequest(string? GiftCode, long ValidStartTime, long ValidEndTime, int MaxUseCnt, AdminAttachment[]? Attachments);

public static class AdminGiftCodeEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/gift-codes", async (AdminGiftCodes giftCodes) => Results.Json(await giftCodes.ListAsync()));
        api.MapPost("/gift-codes", CreateAsync);
        api.MapGet("/gift-codes/{giftCode}/uses", ListUsesAsync);
        api.MapPost("/gift-codes/{giftCode}/enable", (string giftCode, HttpContext context, GiftCodeManager codes, AdminAuditLog audit) =>
            SetEnabledAsync(giftCode, true, context, codes, audit));
        api.MapPost("/gift-codes/{giftCode}/disable", (string giftCode, HttpContext context, GiftCodeManager codes, AdminAuditLog audit) =>
            SetEnabledAsync(giftCode, false, context, codes, audit));
        api.MapDelete("/gift-codes/{giftCode}", DeleteAsync);
    }

    private static async Task<IResult> CreateAsync(AdminGiftCodeRequest request, HttpContext context, AdminGiftCodes giftCodes, AdminAuditLog audit)
    {
        var creation = await giftCodes.CreateAsync(new AdminGiftCodeDraft(
            request.GiftCode ?? string.Empty,
            request.ValidStartTime,
            request.ValidEndTime,
            request.MaxUseCnt,
            request.Attachments ?? []));
        if (creation.GiftCode is not { } created)
        {
            return AdminEndpoints.Refuse(creation.Error ?? "The code cannot be added");
        }

        await audit.RecordAsync(AdminEndpoints.AdminOf(context), "gift code add", created.GiftCode, AdminGiftCodes.Describe(created));
        return Results.Json(created);
    }

    private static async Task<IResult> ListUsesAsync(string giftCode, AdminGiftCodes giftCodes) =>
        await giftCodes.ListUsesAsync(giftCode) is { } uses ? Results.Json(uses) : AdminEndpoints.NotFound();

    private static async Task<IResult> SetEnabledAsync(string giftCode, bool isEnabled, HttpContext context, GiftCodeManager codes, AdminAuditLog audit)
    {
        if (await codes.FindAsync(giftCode) is not { } code)
        {
            return AdminEndpoints.NotFound();
        }

        if (code.IsEnabled != isEnabled && !await codes.SetEnabledAsync(code.GiftCode, isEnabled))
        {
            return AdminEndpoints.NotFound();
        }

        await audit.RecordAsync(
            AdminEndpoints.AdminOf(context),
            isEnabled ? "gift code enable" : "gift code disable",
            code.GiftCode,
            $"enabled {code.IsEnabled} to {isEnabled}");
        return Results.Json(new { giftCode = code.GiftCode, isEnabled });
    }

    private static async Task<IResult> DeleteAsync(string giftCode, HttpContext context, GiftCodeManager codes, AdminAuditLog audit)
    {
        if (await codes.FindAsync(giftCode) is not { } code || !await codes.DeleteAsync(code.GiftCode))
        {
            return AdminEndpoints.NotFound();
        }

        await audit.RecordAsync(AdminEndpoints.AdminOf(context), "gift code delete", code.GiftCode, $"{code.UseCnt} uses removed");
        return Results.Json(new { deleted = true });
    }
}
