using SpiritUNITE.Databin;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.GiftCode;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Admin;

public sealed record AdminGiftCodeDraft(string GiftCode, long ValidStartTime, long ValidEndTime, int MaxUseCnt, IReadOnlyList<AdminAttachment> Attachments);

public sealed record AdminGiftCode(
    string GiftCode,
    long ValidStartTime,
    long ValidEndTime,
    int MaxUseCnt,
    int UseCnt,
    bool IsEnabled,
    long CreateTime,
    IReadOnlyList<AdminAttachment> Attachments);

public sealed record AdminGiftCodeUse(AdminTrainer Trainer, long UseTime, int MailIndex);

public sealed record AdminGiftCodeCreation(AdminGiftCode? GiftCode, string? Error);

public sealed class AdminGiftCodes(GiftCodeManager giftCodes, AdminMail mail, RoleRepository roles, GameData gameData)
{
    public async Task<AdminGiftCodeCreation> CreateAsync(AdminGiftCodeDraft draft)
    {
        var build = mail.Build(new AdminMailDraft(GiftCodeManager.MailSubject, GiftCodeManager.MailContent, draft.Attachments));
        if (build.Mail is not { } systemMail)
        {
            return new AdminGiftCodeCreation(null, build.Error ?? "Rewards cannot be sent");
        }

        var error = await giftCodes.CreateAsync(new GiftCodeDraft(draft.GiftCode, draft.ValidStartTime, draft.ValidEndTime, draft.MaxUseCnt, systemMail.Attachments));
        if (error is not null)
        {
            return new AdminGiftCodeCreation(null, error);
        }

        return await giftCodes.FindAsync(draft.GiftCode) is { } created
            ? new AdminGiftCodeCreation(ToAdmin(created), null)
            : new AdminGiftCodeCreation(null, "The code was removed while it was being added");
    }

    public async Task<AdminGiftCode[]> ListAsync() => [.. (await giftCodes.ListAsync()).Select(ToAdmin)];

    public async Task<AdminGiftCodeUse[]?> ListUsesAsync(string giftCode)
    {
        if (await giftCodes.ListUsesAsync(giftCode) is not { } uses)
        {
            return null;
        }

        var names = await AdminNames.FindAsync(roles, [.. uses.Select(use => use.GameUlongUID)]);
        return [.. uses.Select(use => new AdminGiftCodeUse(names.Trainer(use.GameUlongUID), use.UseTime, use.MailIndex))];
    }

    public static string Describe(AdminGiftCode code) =>
        $"valid from {code.ValidStartTime} until {code.ValidEndTime}, max uses {code.MaxUseCnt}, rewards " +
        string.Join(", ", code.Attachments.Select(attachment => $"{attachment.Kind} {attachment.Id} x{attachment.Count}"));

    private AdminGiftCode ToAdmin(GiftCodeEntry entry) => new(
        entry.GiftCode,
        entry.ValidStartTime,
        entry.ValidEndTime,
        entry.MaxUseCnt,
        entry.UseCnt,
        entry.IsEnabled,
        entry.CreateTime,
        [.. entry.Awards.Select(award => new AdminAttachment(KindOf(award), award.ResID, award.ResCnt))]);

    private string KindOf(pbCOMDT_ONE_RESOURCE_AWARD_INFO award) => (PB_ENUM_RES_RESOURCES_TYPE)award.ResType switch
    {
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin => "coin",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon => "pokemon",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem when UniteLicense.IsUniteLicense(gameData, award.ResID) => "license",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem => "item",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPaster => "holowear",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar => "outfit",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadicon => "icon",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadFrame => "frame",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesLobbyBackground => "backdrop",
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesBadge => "emblem",
        _ => "other",
    };
}
