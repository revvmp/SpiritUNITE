namespace SpiritUNITE.Game.Activity;

public interface IActivityRepository
{
    Task RecordLoginDayAsync(ulong gameUlongUID, DateOnly loginDay);

    Task<DateOnly[]> FindLoginDaysAsync(ulong gameUlongUID, DateOnly since);

    Task<ActivityTaskReward[]> FindTaskRewardsAsync(ulong gameUlongUID, long periodStartSince);

    Task<bool> TryRecordTaskRewardAsync(ulong gameUlongUID, ActivityTaskReward reward);

    Task RemoveTaskRewardAsync(ulong gameUlongUID, ActivityTaskReward reward);
}

public sealed record ActivityTaskReward(int TaskId, long PeriodStart, long RewardTime);
