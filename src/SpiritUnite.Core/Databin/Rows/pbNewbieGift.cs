namespace SpiritUnite.Databin;

public sealed record pbNewbieGift(int SchemeId, int GiftID, int SDropID, int ShowType = 0)
{
    public static long Key(int giftID, int schemeId) => ((long)giftID << 32) | (uint)schemeId;
}
