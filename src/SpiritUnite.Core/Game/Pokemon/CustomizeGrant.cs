using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Pokemon;

public static class CustomizeGrant
{
    private const int PermanentBuyNum = 1;
    private const int NoExclusivePaster = 0;

    public static RoleItemType? FindItemType(GameData gameData, int resID) =>
        CosmeticGrant.FindItemType(gameData, resID) is { } itemType && itemType is RoleItemType.NewPersonaliseButton or RoleItemType.GloryAnnounce
            ? itemType
            : null;

    public static PB_ENUM_RES_ERROR_CODE? AddShopGrant(
        GameData gameData,
        LMNRoleInfo role,
        int resID,
        int num,
        long gainTime,
        List<RoleItemInfo> items)
    {
        if (FindItemType(gameData, resID) is not { } itemType)
        {
            return null;
        }

        if (num != PermanentBuyNum)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyNumFail;
        }

        if (role.OwnsItem(itemType, resID) || items.Any(item => item.ItemType == itemType && item.ItemId == resID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopItemExist;
        }

        if (itemType == RoleItemType.NewPersonaliseButton
            && gameData.PersonaliseButton.Find(resID)!.PokemonAvatarId is not NoExclusivePaster and var pasterId
            && !role.OwnsItem(RoleItemType.Paster, pasterId)
            && !items.Any(item => item.ItemType == RoleItemType.Paster && item.ItemId == pasterId))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrPersonaliseButtonNotPasterCanNotBuy;
        }

        items.Add(new RoleItemInfo(itemType, resID, gainTime, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }
}
