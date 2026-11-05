namespace SpiritUNITE.Databin;

public sealed record pbNewbieChallengeTask(int TaskDay, int DayTaskID, IReadOnlyList<int> SubTaskID, int SchemeId)
{
    public static long Key(int schemeId, int taskDay) => ((long)schemeId << 32) | (uint)taskDay;
}
