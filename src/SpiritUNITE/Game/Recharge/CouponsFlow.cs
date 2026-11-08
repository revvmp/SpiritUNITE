using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Recharge;

public static class CouponsFlow
{
    public const int MaxGemUseLogNum = 100;
    public const int ShopSystemType = 3;
    public const int ActivitySystemType = 6;

    private const int NotMysteryShop = 0;
    private const int MysteryShop = 1;

    public static CouponsFlowEntry? Spend(long couponsCost, DateTimeOffset now, int systemType, pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION extraInfo) =>
        couponsCost <= 0
            ? null
            : new CouponsFlowEntry((int)-Math.Min(couponsCost, int.MaxValue), now.ToUnixTimeSeconds(), systemType, extraInfo);

    public static CouponsFlowEntry? SpendInShop(pbCOMDT_SHOP_BUY_INFO shopBuyInfo, GameData gameData, long couponsCost, DateTimeOffset now)
    {
        var shopItems = shopBuyInfo.BuyShopList
            .Select(shopInfo => (Info: shopInfo, Row: gameData.Shop.Find(shopInfo.ShopID)))
            .Where(shop => shop.Row is not null)
            .ToArray();
        var mysteryShop = shopItems.FirstOrDefault(shop => shop.Row!.IsMysteryShop == MysteryShop);
        var shopInfo = new pbCOMDT_COUPONS_BUG_RES_INFO
        {
            ShopType = shopItems.Length == 0 ? shopBuyInfo.ShopType : shopItems[0].Row!.ShopType,
            MysteryShopId = mysteryShop.Row?.ShopID ?? NotMysteryShop,
            ShopFromType = shopBuyInfo.ShopFromType,
        };
        shopInfo.ResListInfo.AddRange(shopItems.Select(shop => new pbCOMDT_COUPONS_BUG_ONE_RES_INFO
        {
            ResID = shop.Row!.ShopResID,
            ResCnt = shop.Info.Num,
        }));
        return Spend(couponsCost, now, ShopSystemType, new pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION { ShopInfo = shopInfo });
    }
}
