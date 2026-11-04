using System.Collections.Concurrent;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Shop;

public sealed record ShopBuyOutcome(
    PB_ENUM_RES_ERROR_CODE Result,
    ShopPurchase? Purchase = null,
    RoleWallet? Wallet = null,
    IReadOnlyList<ShopBuyRecord>? BuyRecords = null);

public sealed class ShopManager(GameData gameData, IShopRepository repository, WalletManager wallets, TimeProvider clock)
{
    private const int Enabled = 0;

    private const int PokemonIdItemParam = 0;
    private const int NoChoice = 0;
    private const int PermanentItemBuyNum = 1;
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
        var changedRecords = new List<ShopBuyRecord>();

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

            result = AddGrants(role, shopItem, shopInfo.Num, now.ToUnixTimeSeconds(), items, pokemonIds);
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

        var wallet = await wallets.LoadAsync(role.GameUlongUID);
        if (coinCosts.Any(coinCost => wallet.GetCoinVal(coinCost.Key) < coinCost.Value))
        {
            return new(PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin);
        }

        var purchase = new ShopPurchase(coinCosts, items, pokemonIds, changedRecords);
        var (commitResult, walletAfter) = await repository.CommitPurchaseAsync(role.GameUlongUID, purchase);
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
            role.GrantPokemon(pokemonId);
        }

        return new(commitResult, purchase, walletAfter, [.. buyRecords.Values]);
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

        if (shopInfo.VoucherID != 0 || shopInfo.DiscountTicketID != 0 || shopInfo.ChooseResId != NoChoice)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var coinType = (PB_ENUM_RES_COIN_TYPE)shopInfo.ResCoinType;
        var walletCoin = CoinID.GetCoinID(coinType);
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
        List<int> pokemonIds)
    {
        if ((PB_ENUM_RES_SHOP_TYPE)shopItem.ShopType == PB_ENUM_RES_SHOP_TYPE.PbResShopTypeGiftPack)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopUnexistsItem;
        }

        var (avatarIDs, lobbyBgId, pokemonId) = ResolveShopRes(shopItem);
        if (avatarIDs.Length == 0 && lobbyBgId is null && pokemonId is null)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrShopUnexistsItem;
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
