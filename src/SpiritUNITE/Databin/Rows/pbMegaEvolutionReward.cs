namespace SpiritUNITE.Databin;

public sealed record pbMegaEvolutionReward(int ActivityID, int AwardKey, int TargetComplete, int AwardDropID, int StateIndex)
{
    public static long Key(int activityID, int awardKey) => ((long)awardKey << 32) | (uint)activityID;
}
