namespace SpiritUNITE.Databin;

public sealed record pbBattlePass_SeasonTask(
    int SeasonID,
    int WeekIndex,
    IReadOnlyList<int> TaskID,
    IReadOnlyList<int> WelfareTaskID,
    int WelfareTaskRefreshNum = 0,
    int WeekMaxExp = 0)
{
    public static long Key(int seasonID, int weekIndex) =>
        ((long)seasonID << 32) | (uint)weekIndex;
}
