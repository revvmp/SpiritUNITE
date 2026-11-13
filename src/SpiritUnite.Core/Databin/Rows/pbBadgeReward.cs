namespace SpiritUnite.Databin;

public sealed record pbBadgeReward(
    int RewardId,
    int LotteryPoolId,
    int RewardProbability,
    int ResourceType,
    int ResourceId,
    int ResourceNum,
    int FirstLottery = 0);
