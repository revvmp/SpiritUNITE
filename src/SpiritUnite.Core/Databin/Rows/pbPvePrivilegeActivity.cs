namespace SpiritUnite.Databin;

public sealed record pbPvePrivilegeActivity(
    int ActivityID,
    int WeekExtraRewardNum,
    int WeekRewardLimit,
    int CostCoupons,
    int UnlockBuffNum,
    int PrivilegeKeepTime);
