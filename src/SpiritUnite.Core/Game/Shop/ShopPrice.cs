using SpiritUnite.Tools;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Shop;

public readonly record struct ShopPrice(int CfgPrice, int FinalPrice)
{
    private const int Enabled = 0;
    private const int AvatarFragmentItemID = 1002000040;
    private const int PasterFragmentItemID = 1002000039;

    public static int? FindFragmentItemID(pbShop shopItem) => (PB_ENUM_RES_SHOP_TYPE)shopItem.ShopType switch
    {
        PB_ENUM_RES_SHOP_TYPE.PbResShopTypeFragmentAvatar => AvatarFragmentItemID,
        PB_ENUM_RES_SHOP_TYPE.PbResShopTypeFragmentPaster => PasterFragmentItemID,
        _ => null,
    };

    public static ShopPrice? GetShopPrice(pbShop shopItem, PB_ENUM_RES_COIN_TYPE coinType, long svrConfTime)
    {
        var (isOffered, cfgPrice, discountPrice) = coinType switch
        {
            PB_ENUM_RES_COIN_TYPE.PbResCoinTypeCookies =>
                (shopItem.IsCookiesBuy != Enabled, shopItem.CookiesBuy, shopItem.DiscountCookiesBuy),
            PB_ENUM_RES_COIN_TYPE.PbResCoinTypePokecoins =>
                (shopItem.IsPokeCoinsBuy != Enabled, shopItem.PokeCoinsBuy, shopItem.DiscountPokeCoinsBuy),
            PB_ENUM_RES_COIN_TYPE.PbResCoinTypeCoupons =>
                (shopItem.IsCouponsBuy != Enabled || shopItem.IsCandysBuy != Enabled, shopItem.CouponsBuy, shopItem.DiscountCouponsBuy),
            PB_ENUM_RES_COIN_TYPE.PbResCoinTypeFrament =>
                (shopItem.IsFramentBuy != Enabled && FindFragmentItemID(shopItem) is not null, shopItem.FramentBuy, shopItem.DiscountFramentBuy),
            _ => (false, 0, 0),
        };

        if (!isOffered || cfgPrice <= 0)
        {
            return null;
        }

        var finalPrice = IsDiscount(shopItem, svrConfTime) && discountPrice > 0 ? discountPrice : cfgPrice;
        return new ShopPrice(cfgPrice, finalPrice);
    }

    public static pbCOMDT_SHOP_PRICE_DETAIL BuildShopPriceDetail(pbShop shopItem) => new()
    {
        ShopID = shopItem.ShopID,
        CfgPriceCoupons = shopItem.CouponsBuy,
        CfgPricePokeCoins = shopItem.PokeCoinsBuy,
        CfgPriceCookies = shopItem.CookiesBuy,
        DiscountPriceCoupons = shopItem.DiscountCouponsBuy,
        DiscountPricePokeCoins = shopItem.DiscountPokeCoinsBuy,
        DiscountPriceCookies = shopItem.DiscountCookiesBuy,
    };

    private static bool IsDiscount(pbShop shopItem, long svrConfTime) =>
        UTCTimeUtil.IsInTime(shopItem.DiscountStartTime, shopItem.DiscountEndTime, svrConfTime);
}
