using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Recharge;

public static class CouponsFlow
{
    public const int MaxGemUseLogNum = 100;
    public const int BattlepassSystemType = 2;
    public const int ShopSystemType = 3;
    public const int MailSystemType = 5;
    public const int ActivitySystemType = 6;
    public const int BadgeSystemType = 8;

    private const int NoLevelsBought = 0;

    private const int NotMysteryShop = 0;
    private const int MysteryShop = 1;

    public static CouponsFlowEntry? Spend(long couponsCost, DateTimeOffset now, int systemType, pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION extraInfo) =>
        couponsCost <= 0
            ? null
            : new CouponsFlowEntry((int)-Math.Min(couponsCost, int.MaxValue), now.ToUnixTimeSeconds(), systemType, extraInfo);

    private static CouponsFlowEntry? SpendCoin(int coinID, long cost, DateTimeOffset now, int systemType, pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION extraInfo) =>
        coinID == CoinID.Coupons ? Spend(cost, now, systemType, extraInfo) : null;

    private static CouponsFlowEntry? Gain(long couponsGain, DateTimeOffset now, int systemType, pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION extraInfo) =>
        couponsGain <= 0
            ? null
            : new CouponsFlowEntry((int)Math.Min(couponsGain, int.MaxValue), now.ToUnixTimeSeconds(), systemType, extraInfo);

    public static CouponsFlowEntry? SpendOnBattlePassPass(int coinID, long cost, DateTimeOffset now, int passType) =>
        SpendOnBattlePass(coinID, cost, now, passType, NoLevelsBought);

    public static CouponsFlowEntry? SpendOnBattlePassLevels(int coinID, long cost, DateTimeOffset now, int passType, int boughtLevels) =>
        SpendOnBattlePass(coinID, cost, now, passType, boughtLevels);

    public static CouponsFlowEntry? SpendOnBadgeSchedue(int coinID, long cost, DateTimeOffset now) =>
        SpendCoin(coinID, cost, now, BadgeSystemType, new pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION());

    public static CouponsFlowEntry? GainFromMail(IReadOnlyDictionary<int, long> coinGrants, DateTimeOffset now) =>
        Gain(coinGrants.GetValueOrDefault(CoinID.Coupons), now, MailSystemType, new pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION());

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

    private static CouponsFlowEntry? SpendOnBattlePass(int coinID, long cost, DateTimeOffset now, int passType, int buyLevel) =>
        SpendCoin(coinID, cost, now, BattlepassSystemType, new pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION
        {
            BpInfo = new pbCOMDT_COUPONS_FLOW_BP_TYPE { PassType = passType, BuyLevel = buyLevel },
        });
}
