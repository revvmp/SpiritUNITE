using System.Collections.Concurrent;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Bag;
using SpiritUnite.Databin;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Tools;
using SpiritUnite.Game.Recharge;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Shop;

public sealed record ShopBuyOutcome(
    PB_ENUM_RES_ERROR_CODE Result,
    ShopPurchase? Purchase = null,
    RoleWallet? Wallet = null,
    IReadOnlyList<ShopBuyRecord>? BuyRecords = null,
    IReadOnlyList<BagItem>? ChangedBagItems = null,
    pbCOMDT_ROLE_AVATAR_INFO? WornRoleAvatarInfo = null);

public sealed class ShopManager(
    GameData gameData,
    ShopRepository repository,
    RoleInfoStore roles,
    WalletManager wallets,
    BagManager bag,
    TimeProvider clock)
{
    private const int Enabled = 0;

    private const int PokemonIdItemParam = 0;
    private const int NoChoice = 0;
    private const int PermanentItemBuyNum = 1;
    private const int BulkBuyAllowed = 1;
    private const int MaxBuyShopCnt = (int)PB_MACRO_COM_MAX_BUY_SHOP_CNT.PbComMaxBuyShopCnt;
    private const int MaxBuyOneShopCnt = (int)PB_MACRO_COM_MAX_BUY_ONE_SHOP_CNT.PbComMaxBuyOneShopCnt;

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> purchaseGates = new();

    public Task<IReadOnlyList<ShopBuyRecord>> GetBuyRecordsAsync(ulong gameUlongUID) =>
        repository.GetBuyRecordsAsync(gameUlongUID);

    public pbCOMDT_SHOP_PRICE_INFO GetShopPrice(IEnumerable<int> shopList) => new()
    {
        ShopList =
        {
            shopList.Distinct()
                .Select(shopID => gameData.Shop.Find(shopID))
                .OfType<pbShop>()
                .Select(ShopPrice.BuildShopPriceDetail),
        },
    };

    public async Task<ShopBuyOutcome> BuyAsync(LMNRoleInfo role, pbCOMDT_SHOP_BUY_INFO shopBuyInfo)
    {
        var gate = purchaseGates.GetOrAdd(role.GameUlongUID, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await BuyLockedAsync(role, shopBuyInfo);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ShopBuyOutcome> BuyLockedAsync(LMNRoleInfo role, pbCOMDT_SHOP_BUY_INFO shopBuyInfo)
    {
        var buyShopList = shopBuyInfo.BuyShopList;
        if (buyShopList.Count is 0 or > MaxBuyShopCnt
            || buyShopList.Select(shopInfo => shopInfo.ShopID).Distinct().Count() != buyShopList.Count)
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrShopBuyNumFail);
        }

        var now = clock.GetUtcNow();
        var svrConfTime = UTCTimeUtil.ServerTime2CfgTime(now);
        var buyRecords = (await repository.GetBuyRecordsAsync(role.GameUlongUID))
            .ToDictionary(record => record.SpecSaleShopID);
        var coinCosts = new Dictionary<int, long>();
        var items = new List<RoleItemInfo>();
        var pokemonIds = new List<int>();
        var outSideItems = new List<BagGrant>();
        var changedRecords = new List<ShopBuyRecord>();
        var beautyAvatarIDs = new List<int>();
        int? beautySkinColorID = null;

        foreach (var shopInfo in buyShopList)
        {
            var shopItem = gameData.Shop.Find(shopInfo.ShopID);
            if (shopItem is null)
            {
                return new(PB_ENUM_RES_ERROR_CODE.PbErrShopUnexists);
            }

            var result = ValidateShopInfo(shopItem, shopInfo, svrConfTime, RoleProfile.GetLevel(gameData, role), out var coinID, out var cost);
            if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                return new(result);
            }

            result = gameData.BeautySkin.Find(shopItem.ShopResID) is { } beautySkin && IsBeautyShop(shopItem)
                ? ChooseBeautySkin(role, beautySkin, shopInfo.Num, ref beautySkinColorID)
                : IsBeautyAvatar(shopItem)
                    ? AddBeautyGrant(role, shopItem, shopInfo.Num, now.ToUnixTimeSeconds(), items, beautyAvatarIDs)
                    : AddGrants(role, shopItem, shopInfo.Num, now.ToUnixTimeSeconds(), items, pokemonIds, outSideItems);
            if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                return new(result);
            }

            result = CountBuyLimit(shopItem, shopInfo.Num, buyRecords.GetValueOrDefault(shopItem.ShopID), now, out var record);
            if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                return new(result);
            }

            if (record is not null)
            {
                changedRecords.Add(record);
                buyRecords[record.SpecSaleShopID] = record;
            }

            coinCosts[coinID] = coinCosts.GetValueOrDefault(coinID) + cost;
        }

        BagCost[] itemCosts =
        [
            .. coinCosts.Where(cost => !CoinID.IsGrantableCoin(cost.Key))
                .Select(cost => new BagCost(RoleItemType.OutSideItem, cost.Key, (int)Math.Min(cost.Value, int.MaxValue))),
        ];
        foreach (var itemCost in itemCosts)
        {
            if (coinCosts[itemCost.ItemId] > int.MaxValue
                || await bag.FindOwnedOutSideItemAsync(role.GameUlongUID, itemCost.ItemId) is not { } owned
                || owned.Cnt < itemCost.Cnt)
            {
                return new(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemFragmentNotEnough);
            }

            coinCosts.Remove(itemCost.ItemId);
        }

        var wallet = await wallets.LoadAsync(role.GameUlongUID);
        if (coinCosts.Any(coinCost => wallet.GetCoinVal(coinCost.Key) < coinCost.Value))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin);
        }

        var purchase = new ShopPurchase(coinCosts, items, pokemonIds, changedRecords, outSideItems)
        {
            ItemCosts = itemCosts,
            CouponsFlow = CouponsFlow.SpendInShop(shopBuyInfo, gameData, coinCosts.GetValueOrDefault(CoinID.Coupons), now),
        };
        var (commitResult, walletAfter, changedBagItems) = await repository.CommitPurchaseAsync(
            role.GameUlongUID,
            purchase,
            now.ToUnixTimeSeconds());
        if (commitResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return new(commitResult);
        }

        foreach (var item in items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }

        foreach (var pokemonId in pokemonIds)
        {
            role.GrantPokemon(
                pokemonId,
                now.ToUnixTimeSeconds(),
                (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonShopadditem);
        }

        var wornRoleAvatarInfo = await WearBeautyAsync(role, beautyAvatarIDs, beautySkinColorID, now.ToUnixTimeSeconds());
        return new(commitResult, purchase, walletAfter, [.. buyRecords.Values], changedBagItems, wornRoleAvatarInfo);
    }

    private static bool IsBeautyShop(pbShop shopItem) =>
        (PB_ENUM_RES_SHOP_TYPE)shopItem.ShopType == PB_ENUM_RES_SHOP_TYPE.PbResShopTypeBeautyShop;

    private bool IsBeautyAvatar(pbShop shopItem) =>
        IsBeautyShop(shopItem) && gameData.AvatarItem.Contains(shopItem.ShopResID);

    private static PB_ENUM_RES_ERROR_CODE ChooseBeautySkin(LMNRoleInfo role, pbBeautySkin beautySkin, int num, ref int? beautySkinColorID)
    {
        if (num != PermanentItemBuyNum)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyNumFail;
        }

        if (beautySkin.Gender != role.Gender || beautySkinColorID is not null)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        if (role.RoleAvatarInfo.SkinColorID == beautySkin.SkinColorID)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopAvatarExists;
        }

        beautySkinColorID = beautySkin.SkinColorID;
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static PB_ENUM_RES_ERROR_CODE AddBeautyGrant(
        LMNRoleInfo role,
        pbShop shopItem,
        int num,
        long gainTime,
        List<RoleItemInfo> items,
        List<int> beautyAvatarIDs)
    {
        if (num != PermanentItemBuyNum)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyNumFail;
        }

        var avatarID = shopItem.ShopResID;
        if (role.RoleAvatarInfo.AvatarIDList.Contains(avatarID) || beautyAvatarIDs.Contains(avatarID))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopAvatarExists;
        }

        AddItemGrant(role, RoleItemType.AvatarItem, avatarID, gainTime, items);
        beautyAvatarIDs.Add(avatarID);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private async Task<pbCOMDT_ROLE_AVATAR_INFO?> WearBeautyAsync(LMNRoleInfo role, List<int> beautyAvatarIDs, int? beautySkinColorID, long now)
    {
        if (beautyAvatarIDs.Count == 0 && beautySkinColorID is null)
        {
            return null;
        }

        if (beautyAvatarIDs.Count > 0)
        {
            var avatarIDList = RoleAvatar.ReplaceSlots(gameData, role.RoleAvatarInfo.AvatarIDList, beautyAvatarIDs);
            if (role.SetRoleAvatar(gameData, avatarIDList, now, out _) != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                return null;
            }
        }

        if (beautySkinColorID is { } skinColorID)
        {
            role.SetSkinColor(skinColorID);
        }

        await roles.SaveAsync(role);
        return role.RoleAvatarInfo;
    }

    private static PB_ENUM_RES_ERROR_CODE ValidateShopInfo(
        pbShop shopItem,
        pbCOMDT_SHOP_INFO shopInfo,
        long svrConfTime,
        int roleLevel,
        out int coinID,
        out long cost)
    {
        coinID = 0;
        cost = 0;
        if (!CanBuy(shopItem, svrConfTime))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopNotOnTime;
        }

        if (shopItem.Level > roleLevel)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopLevelLimit;
        }

        if (shopInfo.Num is < 1 or > MaxBuyOneShopCnt)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyNumFail;
        }

        if (shopInfo.VoucherID != 0 || shopInfo.DiscountTicketID != 0
            || shopInfo.ChooseResId != NoChoice && shopInfo.ChooseResId != shopItem.ShopResID)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var coinType = (PB_ENUM_RES_COIN_TYPE)shopInfo.ResCoinType;
        var walletCoin = coinType == PB_ENUM_RES_COIN_TYPE.PbResCoinTypeFrament
            ? ShopPrice.FindFragmentItemID(shopItem)
            : CoinID.GetCoinID(coinType);
        var price = ShopPrice.GetShopPrice(shopItem, coinType, svrConfTime);
        if (walletCoin is null || price is null)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopCoinTypeUnexists;
        }

        if (shopInfo.CfgPrice != price.Value.CfgPrice || shopInfo.DiscountPrice != price.Value.FinalPrice)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopCfgPriceFail;
        }

        coinID = walletCoin.Value;
        cost = (long)price.Value.FinalPrice * shopInfo.Num;
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static bool CanBuy(pbShop shopItem, long svrConfTime)
    {
        var isOnSale = UTCTimeUtil.IsInTime(shopItem.OnTime, shopItem.OffTime, svrConfTime);
        var isPreSale = shopItem.IsPreView != Enabled
            && UTCTimeUtil.IsInTime(shopItem.PreViewStartTime, shopItem.PreViewEndTime, svrConfTime);
        return isOnSale || isPreSale;
    }

    private PB_ENUM_RES_ERROR_CODE AddGrants(
        LMNRoleInfo role,
        pbShop shopItem,
        int num,
        long gainTime,
        List<RoleItemInfo> items,
        List<int> pokemonIds,
        List<BagGrant> outSideItems)
    {
        if ((PB_ENUM_RES_SHOP_TYPE)shopItem.ShopType == PB_ENUM_RES_SHOP_TYPE.PbResShopTypeGiftPack)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopUnexistsItem;
        }

        if (PasterGrant.IsPaster(gameData, shopItem.ShopResID))
        {
            return AddPasterGrant(role, shopItem, num, gainTime, items);
        }

        var (avatarIDs, lobbyBgId, pokemonId) = ResolveShopRes(shopItem);
        if (avatarIDs.Length == 0 && lobbyBgId is null && pokemonId is null)
        {
            return AddOutSideItemGrant(shopItem, num, outSideItems);
        }

        if (num != PermanentItemBuyNum)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyNumFail;
        }

        if (pokemonId is { } pokemon)
        {
            if (role.OwnsPokemon(pokemon) || pokemonIds.Contains(pokemon))
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrShopItemExist;
            }

            pokemonIds.Add(pokemon);
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        }

        if (lobbyBgId is { } lobbyBg)
        {
            return AddItemGrant(role, RoleItemType.LobbyBg, lobbyBg, gainTime, items)
                ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
                : PB_ENUM_RES_ERROR_CODE.PbErrShopItemExist;
        }

        var granted = avatarIDs.Count(avatarID => AddItemGrant(role, RoleItemType.AvatarItem, avatarID, gainTime, items));
        return granted > 0 ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : PB_ENUM_RES_ERROR_CODE.PbErrShopAvatarExists;
    }

    private static PB_ENUM_RES_ERROR_CODE AddPasterGrant(
        LMNRoleInfo role,
        pbShop shopItem,
        int num,
        long gainTime,
        List<RoleItemInfo> items)
    {
        if ((PB_ENUM_RES_SHOP_TYPE)shopItem.ShopType == PB_ENUM_RES_SHOP_TYPE.PbResShopTypePackPaster)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopUnexistsItem;
        }

        if (num != PermanentItemBuyNum)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyNumFail;
        }

        return AddItemGrant(role, RoleItemType.Paster, shopItem.ShopResID, gainTime, items)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrShopItemExist;
    }

    private PB_ENUM_RES_ERROR_CODE AddOutSideItemGrant(pbShop shopItem, int num, List<BagGrant> outSideItems)
    {
        if ((PB_ENUM_RES_SHOP_TYPE)shopItem.ShopType == PB_ENUM_RES_SHOP_TYPE.PbResShopTypeCandys
            || bag.CreateOutSideItemGrant(shopItem.ShopResID, num) is not { } grant)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopUnexistsItem;
        }

        if (num != PermanentItemBuyNum && shopItem.CanBulkBuy != BulkBuyAllowed)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyNumFail;
        }

        outSideItems.Add(grant);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static bool AddItemGrant(
        LMNRoleInfo role,
        RoleItemType itemType,
        int itemId,
        long gainTime,
        List<RoleItemInfo> items)
    {
        if (role.OwnsItem(itemType, itemId) || items.Any(item => item.ItemType == itemType && item.ItemId == itemId))
        {
            return false;
        }

        items.Add(new RoleItemInfo(itemType, itemId, gainTime, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag));
        return true;
    }

    private (int[] AvatarIDs, int? LobbyBgId, int? PokemonId) ResolveShopRes(pbShop shopItem)
    {
        var shopResID = shopItem.ShopResID;
        if (gameData.AvatarItem.Contains(shopResID))
        {
            return ([shopResID], null, null);
        }

        if (gameData.AvatarSuit.Find(shopResID) is { } avatarSuit)
        {
            int[] contained = [.. avatarSuit.ContianAvatarID.Where(avatarID => gameData.AvatarItem.Contains(avatarID)).Distinct()];
            return (contained, null, null);
        }

        if (gameData.LobbyBg.Contains(shopResID) && shopResID != gameData.DefaultLobbyBgId)
        {
            return ([], shopResID, null);
        }

        if ((PB_ENUM_RES_SHOP_TYPE)shopItem.ShopType == PB_ENUM_RES_SHOP_TYPE.PbResShopTypeCandys
            && gameData.OutSideItemBase.Find(shopResID) is { ItemParam.Count: > PokemonIdItemParam } candy
            && candy.ItemParam[PokemonIdItemParam] > 0)
        {
            return ([], null, candy.ItemParam[PokemonIdItemParam]);
        }

        return ([], null, null);
    }

    private static PB_ENUM_RES_ERROR_CODE CountBuyLimit(
        pbShop shopItem,
        int num,
        ShopBuyRecord? previous,
        DateTimeOffset now,
        out ShopBuyRecord? record)
    {
        record = null;
        var buyLimitType = (PB_ENUM_RES_BUY_LIMIT_TYPE)shopItem.BuyLimitType;
        if (buyLimitType == PB_ENUM_RES_BUY_LIMIT_TYPE.PbResBuyLimitTypeInvalid)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        }

        if (buyLimitType is not (PB_ENUM_RES_BUY_LIMIT_TYPE.PbResBuyLimitTypeDay
            or PB_ENUM_RES_BUY_LIMIT_TYPE.PbResBuyLimitTypeWeek
            or PB_ENUM_RES_BUY_LIMIT_TYPE.PbResBuyLimitTypePermament))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyLimit;
        }

        var boughtNum = previous is not null && IsSameBuyCycle(buyLimitType, previous.SpecSaleCycleTime, now)
            ? previous.SpecSaleNum
            : 0;
        if (boughtNum + num > shopItem.BuyLimitNum)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopBuyLimit;
        }

        record = new ShopBuyRecord(shopItem.ShopID, boughtNum + num, now.ToUnixTimeSeconds());
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    private static bool IsSameBuyCycle(PB_ENUM_RES_BUY_LIMIT_TYPE buyLimitType, long specSaleCycleTime, DateTimeOffset now)
    {
        var lastBuy = DateTimeOffset.FromUnixTimeSeconds(specSaleCycleTime).UtcDateTime;
        var today = now.UtcDateTime;
        return buyLimitType switch
        {
            PB_ENUM_RES_BUY_LIMIT_TYPE.PbResBuyLimitTypeDay => lastBuy.Date == today.Date,
            PB_ENUM_RES_BUY_LIMIT_TYPE.PbResBuyLimitTypeWeek =>
                lastBuy.Year == today.Year && MondayWeekOfYear(lastBuy) == MondayWeekOfYear(today),
            _ => true,
        };
    }

    private static int MondayWeekOfYear(DateTime date)
    {
        const int DaysPerWeek = 7;
        var daysSinceMonday = ((int)date.DayOfWeek + DaysPerWeek - (int)DayOfWeek.Monday) % DaysPerWeek;
        return (date.DayOfYear - 1 + DaysPerWeek - daysSinceMonday) / DaysPerWeek;
    }
}
