using SpiritUNITE.Databin;
using SpiritUNITE.Game.Badge;
using SpiritUNITE.Game.Mail;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Online;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Game.Admin;

public sealed record AdminGiveAllResult(int Available, int Granted);

public sealed class AdminGiveAll(
    GameData gameData,
    RoleInfoStore roles,
    MailManager mail,
    BadgeManager badges,
    GrantUpdates grantUpdates,
    LobbyRoles lobbyRoles,
    TimeProvider clock)
{
    public static readonly string[] Categories = ["pokemon", "holowear", "outfit", "icon", "frame", "backdrop", "emblem"];

    private const int SingleCount = 1;
    private const int Shown = 1;
    private const long NotScheduled = 0;

    public static bool IsCategory(string category) => Categories.Contains(category);

    public async Task<AdminGiveAllResult?> GiveAsync(ulong uid, string category)
    {
        if (!IsCategory(category) || await roles.FindAsync(uid) is not { } role)
        {
            return null;
        }

        var available = AwardsOf(category, UTCTimeUtil.ServerTime2CfgTime(clock.GetUtcNow()));
        var ownedEmblems = category == "emblem" ? (await badges.GetOwnedBadgesAsync(uid)).Select(badge => badge.ItemId).ToHashSet() : [];
        pbCOMDT_ONE_RESOURCE_AWARD_INFO[] missing = [.. available.Where(award => !Owns(role, award, ownedEmblems))];
        if (missing.Length == 0)
        {
            return new AdminGiveAllResult(available.Length, 0);
        }

        if (await mail.GrantDirectAsync(role, missing) is not { } outcome)
        {
            return null;
        }

        if (lobbyRoles.IsOnline(uid))
        {
            await lobbyRoles.PushAsync(uid, await grantUpdates.BuildAsync(role, [outcome], withAwardNotify: false));
        }

        return new AdminGiveAllResult(available.Length, missing.Length);
    }

    private static bool Owns(LMNRoleInfo role, pbCOMDT_ONE_RESOURCE_AWARD_INFO award, IReadOnlySet<int> ownedEmblems) => (PB_ENUM_RES_RESOURCES_TYPE)award.ResType switch
    {
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesBadge => ownedEmblems.Contains(award.ResID),
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon => role.OwnsPokemon(award.ResID),
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPaster => role.OwnsItem(RoleItemType.Paster, award.ResID),
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar => role.OwnsItem(RoleItemType.AvatarItem, award.ResID),
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadicon => role.OwnsItem(RoleItemType.HeadIcon, award.ResID),
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadFrame => role.OwnsItem(RoleItemType.PersonStyleHeadFrame, award.ResID),
        _ => role.OwnsItem(RoleItemType.LobbyBg, award.ResID),
    };

    private pbCOMDT_ONE_RESOURCE_AWARD_INFO[] AwardsOf(string category, long now) => category switch
    {
        "pokemon" => Awards(
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPokemon,
            gameData.PokemonBase.Rows
                .Where(row => row.HideInBox == 0 && (row.OriginalForm == 0 || row.OriginalForm == row.PokemonId))
                .Select(row => row.PokemonId)),
        "holowear" => Awards(
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPaster,
            gameData.PokemonAvatarBase.Rows.Where(row => row.OnTime <= now).Select(row => row.PokemonAvatarId)),
        "outfit" => Awards(
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar,
            gameData.AvatarItem.Rows
                .Where(row => row.AvatarName.Length > 0 && IsReleasedOutfit(row, now))
                .Select(row => row.AvatarItemId)),
        "icon" => Awards(
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadicon,
            gameData.HeadIconBase.Rows.Where(row => row.IsOpen == Shown && row.OnTime <= now).Select(row => row.HeadIconID)),
        "frame" => Awards(
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadFrame,
            gameData.PersonStyleHeadFrame.Rows.Where(row => row.IsOpen == Shown && row.OnTime <= now).Select(row => row.HeadIconID)),
        "emblem" => Awards(
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesBadge,
            gameData.BadgeBase.Rows.Select(row => row.BadgeId)),
        _ => Awards(
            PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesLobbyBackground,
            gameData.LobbyBg.Rows
                .Where(row => row.LobbyBgID != gameData.DefaultLobbyBgId && row.OnTime <= now)
                .Select(row => row.LobbyBgID)),
    };

    private static bool IsReleasedOutfit(pbAvatarItem row, long now) =>
        row.StageClothOnTime.FirstOrDefault(time => time != NotScheduled) <= now;

    private static pbCOMDT_ONE_RESOURCE_AWARD_INFO[] Awards(PB_ENUM_RES_RESOURCES_TYPE resourceType, IEnumerable<int> ids) =>
    [
        .. ids.Distinct().Select(id => new pbCOMDT_ONE_RESOURCE_AWARD_INFO { ResType = (int)resourceType, ResID = id, ResCnt = SingleCount }),
    ];
}
