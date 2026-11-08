namespace SpiritUnite.Databin;

public sealed record pbGuildActivity_ScoreReward(int RewardID, int GearID, int SDropID)
{
    public static long Key(int rewardID, int gearID) => ((long)rewardID << 32) | (uint)gearID;
}
