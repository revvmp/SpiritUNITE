using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Shop;

public sealed record ShopBuyRecord(int SpecSaleShopID, int SpecSaleNum, long SpecSaleCycleTime)
{
    public pbCOMDT_SPECIAL_SALE_DETAIL ToSpecialSaleDetail() => new()
    {
        SpecSaleShopID = SpecSaleShopID,
        SpecSaleNum = SpecSaleNum,
        SpecSaleCycleTime = SpecSaleCycleTime,
    };

    public static pbCOMDT_SPECIAL_SALE_INFO BuildSpecialSaleInfo(IEnumerable<ShopBuyRecord> records) => new()
    {
        SpecSaleInfo = { records.OrderBy(record => record.SpecSaleShopID).Select(record => record.ToSpecialSaleDetail()) },
    };
}
