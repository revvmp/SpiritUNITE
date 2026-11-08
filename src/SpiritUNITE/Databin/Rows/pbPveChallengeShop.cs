namespace SpiritUNITE.Databin;

public sealed record pbPveChallengeShop(
    int PeriodID,
    int ShopID,
    int BuyNumLimit,
    int ResID,
    int IsBuff,
    int GroupId,
    int Level,
    int Score = 0,
    int JumpLink = 0);
