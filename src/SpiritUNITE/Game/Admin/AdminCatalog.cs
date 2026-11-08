using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Admin;

public sealed record AdminCatalogEntry(int Id, string Name, string Type, int PokemonId = 0);

public sealed record AdminCatalogData(
    IReadOnlyList<AdminCatalogEntry> Coins,
    IReadOnlyList<AdminCatalogEntry> Pokemon,
    IReadOnlyList<AdminCatalogEntry> Licenses,
    IReadOnlyList<AdminCatalogEntry> Holowear,
    IReadOnlyList<AdminCatalogEntry> Items,
    IReadOnlyList<AdminCatalogEntry> Outfits,
    IReadOnlyList<AdminCatalogEntry> Icons,
    IReadOnlyList<AdminCatalogEntry> Frames,
    IReadOnlyList<AdminCatalogEntry> Backdrops,
    IReadOnlyList<AdminCatalogEntry> Emblems,
    IReadOnlyList<AdminCatalogEntry> Maps);

public sealed partial class AdminCatalog(GameData gameData, GameNames names)
{
    private const string OutSideItemTypePrefix = "PbResOutsideitemType";
    private const string ItemType = "Item";
    private const string CoinType = "Currency";
    private const string LicensePointsType = "License points";
    private const string EventTokenType = "Event token";
    private const string LicenseType = "Unite license";
    private const string HolowearType = "Holowear";
    private const string OutfitType = "Outfit";
    private const string IconType = "Icon";
    private const string FrameType = "Frame";
    private const string BackdropType = "Backdrop";
    private const string MapType = "Map";
    private const string EmblemType = "Emblem";
    private const string BadgeQualityPrefix = "PbResBadgeQualityType";
    private const string PokemonType = "Pokemon";
    private const int PokemonParamIndex = 0;

    private static readonly FrozenDictionary<int, string> WalletCoinNames = new Dictionary<int, string>
    {
        [CoinID.Cookies] = "Aeos coins",
        [CoinID.PokeCoins] = "Aeos tickets",
        [CoinID.Coupons] = "Aeos gems",
        [CoinID.UNIVERSAL_SHARDS_TOKEN_ID] = "Universal license points",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> OutSideItemTypeNames = new Dictionary<string, string>
    {
        ["Common"] = ItemType,
        ["Helditem"] = "Held item",
        ["Traineritem"] = "Trainer item",
        ["Renamecard"] = "Rename card",
        ["Optionalchest"] = "Optional chest",
        ["Pastertimelimited"] = "Holowear trial",
        ["Pokemontimelimited"] = "Pokemon trial",
        ["Avatartimelimited"] = "Avatar trial",
        ["Pasterfragment"] = "Holowear fragment",
        ["Avatarfragment"] = "Avatar fragment",
        ["Pokemonavatarvoucher"] = "Holowear voucher",
        ["Energyaccelerate"] = "Energy accelerate",
        ["Energypaycapacity"] = "Energy capacity",
        ["Multibuff"] = "Multi buff",
        ["ExclusiveStarRoadScore"] = LicensePointsType,
        ["ActivityLimitTime"] = EventTokenType,
    }.ToFrozenDictionary();

    private readonly Lazy<AdminCatalogData> data = new(() => Build(gameData, names));

    public AdminCatalogData Data => data.Value;

    private static AdminCatalogData Build(GameData gameData, GameNames names)
    {
        string PokemonName(int pokemonId) =>
            names.Find(GameNames.PokemonGroup, pokemonId) ?? $"{PokemonType} {pokemonId.ToString(CultureInfo.InvariantCulture)}";

        string ItemName(int itemId, string fallback) => names.Find(GameNames.OutSideItemGroup, itemId) ?? fallback;

        var licenses = gameData.OutSideItemBase.Rows
            .Select(row => (row.OutSideItemId, PokemonId: UniteLicense.FindPokemonId(gameData, row.OutSideItemId)))
            .Where(license => license.PokemonId is not null)
            .Select(license => new AdminCatalogEntry(
                license.OutSideItemId,
                ItemName(license.OutSideItemId, $"{PokemonName(license.PokemonId!.Value)} license"),
                LicenseType,
                license.PokemonId.Value))
            .ToArray();
        var licenseIds = licenses.Select(license => license.Id).ToHashSet();
        return new AdminCatalogData(
            Coins: [.. Coins(gameData, names, PokemonName)],
            Pokemon: [.. gameData.PokemonBase.Rows.Select(row => new AdminCatalogEntry(row.PokemonId, PokemonName(row.PokemonId), PokemonType, row.PokemonId)).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)],
            Licenses: [.. licenses.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)],
            Holowear: [.. gameData.PokemonAvatarBase.Rows
                .Select(row => new AdminCatalogEntry(
                    row.PokemonAvatarId,
                    names.Find(GameNames.PokemonAvatarGroup, row.PokemonAvatarId) ?? $"{PokemonName(row.PokemonId)} holowear",
                    HolowearType,
                    row.PokemonId))
                .OrderBy(entry => PokemonName(entry.PokemonId), StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Id)],
            Items: [.. gameData.OutSideItemBase.Rows
                .Where(row => !licenseIds.Contains(row.OutSideItemId)
                    && CoinID.GetResIDType(row.OutSideItemId) == PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem)
                .Select(row => (Row: row, Type: OutSideItemTypeName(row.OutSideItemType)))
                .Select(item => new AdminCatalogEntry(item.Row.OutSideItemId, ItemName(item.Row.OutSideItemId, item.Type), item.Type))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Id)],
            Outfits: Named(gameData.AvatarItem.Rows.Select(row => row.AvatarItemId), names, GameNames.AvatarItemGroup, OutfitType),
            Icons: Named(gameData.HeadIconBase.Rows.Select(row => row.HeadIconID), names, GameNames.HeadIconGroup, IconType),
            Frames: Named(gameData.PersonStyleHeadFrame.Rows.Select(row => row.HeadIconID), names, GameNames.HeadFrameGroup, FrameType),
            Backdrops: Named(
                gameData.LobbyBg.Rows.Select(row => row.LobbyBgID).Where(id => id != gameData.DefaultLobbyBgId),
                names,
                GameNames.LobbyBgGroup,
                BackdropType),
            Emblems: [.. gameData.BadgeBase.Rows
                .Select(row => new AdminCatalogEntry(
                    row.BadgeId,
                    $"{names.Find(GameNames.BadgeGroup, row.BadgeId) ?? EmblemType} ({AdminNames.FromEnum((PB_ENUM_RES_BADGE_QUALITY_TYPE)row.BadgeQuality, BadgeQualityPrefix)})",
                    EmblemType))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Id)],
            Maps: Named(gameData.SceneMap.Rows.Select(row => row.SceneId), names, GameNames.SceneMapGroup, MapType));
    }

    private static IEnumerable<AdminCatalogEntry> Coins(GameData gameData, GameNames names, Func<int, string> pokemonName) =>
        CoinID.WalletCoins
            .Concat(gameData.OutSideItemBase.Rows.Select(row => row.OutSideItemId).Where(CoinID.IsSpecialCoin))
            .Distinct()
            .Select(coinId =>
            {
                var row = gameData.OutSideItemBase.Find(coinId);
                var type = row is null ? CoinType : CoinTypeName(row.OutSideItemType);
                var name = WalletCoinNames.GetValueOrDefault(coinId)
                    ?? names.Find(GameNames.OutSideItemGroup, coinId)
                    ?? (type == LicensePointsType && row?.ItemParam.ElementAtOrDefault(PokemonParamIndex) is > 0 and var pokemonId
                        ? $"{pokemonName(pokemonId)} license points"
                        : type);
                return new AdminCatalogEntry(coinId, name, type, type == LicensePointsType ? row?.ItemParam.ElementAtOrDefault(PokemonParamIndex) ?? 0 : 0);
            })
            .OrderBy(entry => !CoinID.WalletCoins.Contains(entry.Id) && entry.Id != CoinID.UNIVERSAL_SHARDS_TOKEN_ID)
            .ThenBy(entry => entry.Type, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    private static AdminCatalogEntry[] Named(IEnumerable<int> ids, GameNames names, string group, string type) =>
    [
        .. ids.Distinct()
            .Select(id => new AdminCatalogEntry(id, names.Find(group, id) ?? type, type))
            .OrderBy(entry => entry.Name == type)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Id),
    ];

    private static string CoinTypeName(int outSideItemType) => OutSideItemTypeName(outSideItemType) switch
    {
        LicensePointsType => LicensePointsType,
        EventTokenType => EventTokenType,
        _ => CoinType,
    };

    private static string OutSideItemTypeName(int outSideItemType)
    {
        var name = ((PB_ENUM_RES_OUTSIDEITEM_TYPE)outSideItemType).ToString();
        if (!name.StartsWith(OutSideItemTypePrefix, StringComparison.Ordinal))
        {
            return ItemType;
        }

        var shortName = name[OutSideItemTypePrefix.Length..];
        if (OutSideItemTypeNames.TryGetValue(shortName, out var knownName))
        {
            return knownName;
        }

        var words = WordBoundary().Replace(shortName, " $1").ToLowerInvariant();
        return words.Length == 0 ? ItemType : char.ToUpperInvariant(words[0]) + words[1..];
    }

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex WordBoundary();
}
