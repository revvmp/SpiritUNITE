using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Bag;

public sealed record CosmeticTransmute(int Transmuteid, int TransmuteNum);

public sealed record CosmeticGrant(
    RoleItemInfo? Item,
    BagGrant? TransmuteItem,
    int TransmuteCoinID,
    long TransmuteCoinNum,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards)
{
    private const int ItemsPerGrant = 1;
    private const int Replaced = 1;

    public void AddTo(
        ICollection<RoleItemInfo> items,
        ICollection<BagGrant> bagGrants,
        IDictionary<int, long> coins,
        List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> awards)
    {
        if (Item is not null)
        {
            items.Add(Item);
        }

        if (TransmuteItem is not null)
        {
            bagGrants.Add(TransmuteItem);
        }

        if (TransmuteCoinNum > 0)
        {
            coins[TransmuteCoinID] = coins.TryGetValue(TransmuteCoinID, out var planned) ? planned + TransmuteCoinNum : TransmuteCoinNum;
        }

        awards.AddRange(Awards);
    }

    public static RoleItemType? FindItemType(GameData gameData, int resID) => CoinID.GetResIDType(resID) switch
    {
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesPaster when gameData.PokemonAvatarBase.Contains(resID) => RoleItemType.Paster,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar when gameData.AvatarItem.Contains(resID) => RoleItemType.AvatarItem,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadicon when gameData.HeadIconBase.Contains(resID) => RoleItemType.HeadIcon,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesContestantCardRecoration when gameData.ContestantCardDecoration.Contains(resID) =>
            RoleItemType.ContestantCardDecoration,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesSkilleffectsexpandpack when gameData.SkillEffectExpandPackBase.Contains(resID) =>
            RoleItemType.SkillEffectsExpandPack,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesHeadFrame when gameData.PersonStyleHeadFrame.Contains(resID) =>
            RoleItemType.PersonStyleHeadFrame,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCardFace when gameData.CardFace.Contains(resID) => RoleItemType.CardFace,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCanopy when gameData.CanopyBase.Contains(resID) => RoleItemType.Canopy,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesNewPersonaliseButton when gameData.PersonaliseButton.Contains(resID) =>
            RoleItemType.NewPersonaliseButton,
        PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesGloryAnnounce when gameData.GloryAnnounce.Contains(resID) => RoleItemType.GloryAnnounce,
        _ => null,
    };

    public static CosmeticTransmute? FindTransmute(GameData gameData, RoleItemType itemType, int itemId) => itemType switch
    {
        RoleItemType.Paster => gameData.PokemonAvatarBase.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        RoleItemType.AvatarItem => gameData.AvatarItem.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        RoleItemType.HeadIcon => gameData.HeadIconBase.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        RoleItemType.ContestantCardDecoration => gameData.ContestantCardDecoration.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        RoleItemType.SkillEffectsExpandPack => gameData.SkillEffectExpandPackBase.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        RoleItemType.PersonStyleHeadFrame => gameData.PersonStyleHeadFrame.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        RoleItemType.CardFace => gameData.CardFace.Find(itemId) is { } row ? new(row.TransmuteID, row.TransmuteNum) : null,
        RoleItemType.Canopy => gameData.CanopyBase.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        RoleItemType.NewPersonaliseButton => gameData.PersonaliseButton.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        RoleItemType.GloryAnnounce => gameData.GloryAnnounce.Find(itemId) is { } row ? new(row.Transmuteid, row.TransmuteNum) : null,
        _ => null,
    };

    public static CosmeticGrant? Plan(
        GameData gameData,
        BagManager bag,
        LMNRoleInfo role,
        int resID,
        int resNum,
        long gainTime,
        IEnumerable<RoleItemInfo> plannedItems)
    {
        if (resNum <= 0 || FindItemType(gameData, resID) is not { } itemType)
        {
            return null;
        }

        var owned = role.OwnsItem(itemType, resID) || plannedItems.Any(item => item.ItemType == itemType && item.ItemId == resID);
        var item = owned ? null : new RoleItemInfo(itemType, resID, gainTime, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag);
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        if (item is not null)
        {
            awards.Add(item.ToAwardReplaceInfo());
        }

        var transmutedCopies = item is null ? resNum : resNum - ItemsPerGrant;
        if (transmutedCopies == 0)
        {
            return new CosmeticGrant(item, null, 0, 0, awards);
        }

        if (FindTransmute(gameData, itemType, resID) is not { TransmuteNum: > 0 } transmute
            || (long)transmute.TransmuteNum * transmutedCopies is var transmuteNum && transmuteNum > int.MaxValue)
        {
            return null;
        }

        var origin = new pbCOMDT_ONE_RESOURCE_AWARD_INFO { ResID = resID, ResCnt = transmutedCopies, ResType = (int)CoinID.GetResIDType(resID) };
        if (CoinID.IsGrantableCoin(transmute.Transmuteid))
        {
            awards.Add(TransmutedAward(origin, transmute.Transmuteid, (int)transmuteNum, CoinID.GetCoinResourceType(transmute.Transmuteid)));
            return new CosmeticGrant(item, null, transmute.Transmuteid, transmuteNum, awards);
        }

        if (bag.CreateOutSideItemGrant(transmute.Transmuteid, (int)transmuteNum) is not { } transmuteItem)
        {
            return null;
        }

        awards.Add(TransmutedAward(origin, transmute.Transmuteid, (int)transmuteNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
        return new CosmeticGrant(item, transmuteItem, 0, 0, awards);
    }

    private static pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO TransmutedAward(
        pbCOMDT_ONE_RESOURCE_AWARD_INFO origin,
        int transmuteID,
        int transmuteNum,
        PB_ENUM_RES_RESOURCES_TYPE transmuteType) => new()
        {
            IsReplaced = Replaced,
            FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO { ResID = transmuteID, ResCnt = transmuteNum, ResType = (int)transmuteType },
            OriginAwardInfo = origin,
        };
}
