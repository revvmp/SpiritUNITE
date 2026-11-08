namespace SpiritUnite.Databin;

public sealed record pbMysteryShopDiscount(int ActivityID, int Discount, int AreaID = pbMysteryShopDiscount.NoArea)
{
    public const int NoArea = 0;

    public static long Key(int areaID, int activityID) => ((long)areaID << 32) | (uint)activityID;
}
