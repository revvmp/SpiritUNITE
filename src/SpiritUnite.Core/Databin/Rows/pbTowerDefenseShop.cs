namespace SpiritUnite.Databin;

public sealed record pbTowerDefenseShop(
    int ShopID,
    int PeriodID,
    int ResID,
    int Score,
    int BuyNumLimit,
    int IsBuff = 0,
    int GameLevel = 0,
    int GroupId = 0,
    int Level = 0,
    long OnTime = 0,
    long OffTime = 0)
{
    public static long Key(int shopID, int periodID) => ((long)shopID << 32) | (uint)periodID;
}
