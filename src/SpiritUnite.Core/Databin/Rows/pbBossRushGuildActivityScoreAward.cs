namespace SpiritUnite.Databin;

public sealed record pbBossRushGuildActivityScoreAward(int RewardID, int GearID, int Score, int SDropID)
{
    public static long Key(int gearID, int rewardID) => ((long)gearID << 32) | (uint)rewardID;
}
