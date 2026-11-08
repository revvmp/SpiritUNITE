using System.Text;
using SpiritUNITE.Databin;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Online;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Admin;

public sealed record AdminAttachment(string Kind, int Id, int Count);

public sealed record AdminMailDraft(string Subject, string Content, IReadOnlyList<AdminAttachment> Attachments);

public sealed record AdminMailBuild(SystemMail? Mail, string? Error);

public sealed class AdminMail(MailManager mail, RoleRepository roles, LobbyRoles lobbyRoles, GameData gameData)
{
    public const int MaximumCount = 999999;
    public const int MaximumAttachments = MailBox.MaxMailAccess;
    public const int MaximumSubjectBytes = MailBox.MaxMailSubject;
    public const int MaximumContentBytes = MailBox.MaxMailContent;
    private const int SingleCount = 1;

    private static readonly string[] UniqueKinds = ["pokemon", "holowear", "outfit", "icon", "frame", "backdrop"];

    public AdminMailBuild Build(AdminMailDraft draft)
    {
        var subject = draft.Subject.Trim();
        var content = draft.Content.Trim();
        if (subject.Length == 0 || Encoding.UTF8.GetByteCount(subject) > MaximumSubjectBytes)
        {
            return new AdminMailBuild(null, "Subject is empty or too long");
        }

        if (Encoding.UTF8.GetByteCount(content) > MaximumContentBytes)
        {
            return new AdminMailBuild(null, "Message is too long");
        }

        if (draft.Attachments.Count > MaximumAttachments)
        {
            return new AdminMailBuild(null, $"At most {MaximumAttachments} attachments");
        }

        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_INFO>();
        foreach (var attachment in draft.Attachments)
        {
            if (ToAward(attachment) is not { } award || !mail.CanAttach(award))
            {
                return new AdminMailBuild(null, $"{attachment.Kind} {attachment.Id} x{attachment.Count} cannot be sent");
            }

            awards.Add(award);
        }

        return new AdminMailBuild(new SystemMail(subject, content, awards), null);
    }

    public async Task SendAsync(ulong uid, SystemMail systemMail) => await mail.SendSystemMailAsync(uid, systemMail);

    public async Task<int> BroadcastAsync(SystemMail systemMail, bool onlineOnly)
    {
        var uids = onlineOnly ? [.. lobbyRoles.OnlineUids] : await roles.ListUidsAsync();
        var registered = onlineOnly ? (await roles.FindNamesAsync(uids)).Select(role => role.GameUlongUID).ToArray() : uids;
        foreach (var uid in registered)
        {
            await mail.SendSystemMailAsync(uid, systemMail);
        }

        return registered.Length;
    }

    public static bool IsUniqueKind(string kind) => UniqueKinds.Contains(kind);

    private pbCOMDT_ONE_RESOURCE_AWARD_INFO? ToAward(AdminAttachment attachment)
    {
        if (attachment.Count is < SingleCount or > MaximumCount || (IsUniqueKind(attachment.Kind) && attachment.Count != SingleCount))
        {
            return null;
        }

        PB_ENUM_RES_RESOURCES_TYPE? resourceType = attachment.Kind switch
        {
            "coin" when CoinID.IsGrantableCoin(attachment.Id) => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin,
            "pokemon" when gameData.PokemonBase.Contains(attachment.Id) => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon,
            "license" when UniteLicense.IsUniteLicense(gameData, attachment.Id) => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem,
            "holowear" => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPaster,
            "item" when !UniteLicense.IsUniteLicense(gameData, attachment.Id) => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem,
            "outfit" => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar,
            "icon" => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadicon,
            "frame" => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadFrame,
            "backdrop" => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesLobbyBackground,
            "emblem" when gameData.BadgeBase.Contains(attachment.Id) => PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesBadge,
            _ => null,
        };
        return resourceType is { } type
            ? new pbCOMDT_ONE_RESOURCE_AWARD_INFO { ResType = (int)type, ResID = attachment.Id, ResCnt = attachment.Count }
            : null;
    }
}
