namespace SpiritUNITE.Databin;

public sealed record pbMegaEvolutionOther(int ActivityID, int OtherType, int OtherVal = 0)
{
    public static long Key(int activityID, int otherType) => ((long)otherType << 32) | (uint)activityID;
}
