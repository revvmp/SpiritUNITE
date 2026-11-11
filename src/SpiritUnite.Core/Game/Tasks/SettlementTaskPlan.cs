using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Tasks;

public enum SettlementTaskCompleteTime
{
    Stored,
    StoredAfterReceived,
    Received,
    Reward,
}

public sealed record SettlementTaskTemplate(
    pbCOMDT_TASK_INFO Info,
    long PeriodStart,
    int[] Targets,
    int[] StoredConditionIndexes,
    SettlementTaskCompleteTime CompleteTime,
    bool HideNewTagOnComplete = false);

public sealed record SettlementTaskPlan(
    string Effect,
    long Now,
    TaskProcessIncrement[] Increments,
    SettlementTaskTemplate[] Tasks)
{
    public static SettlementTaskPlan Empty(string effect, long now) => new(effect, now, [], []);
}
