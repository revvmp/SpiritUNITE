using SpiritUNITE.Tools;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Shop;

public readonly record struct ShopPrice(int CfgPrice, int FinalPrice)
{
    private const int Enabled = 0;

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
