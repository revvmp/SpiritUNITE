namespace SpiritUnite.Databin;

public sealed record pbActivityScheme(int ActivityID, int SchemeId, int BusinessType, IReadOnlyList<int> TaskGroupIDList)
{
    public static long Key(int activityID, int schemeId) => ((long)schemeId << 32) | (uint)activityID;
}
