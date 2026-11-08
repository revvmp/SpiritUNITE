namespace SpiritUnite.Databin;

public sealed record pbEeveeChallenge(int ActivityID, int EeveeID, int[] TaskIDList)
{
    public static long Key(int eeveeID, int activityID) => ((long)eeveeID << 32) | (uint)activityID;
}
