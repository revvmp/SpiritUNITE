using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Activity;

namespace SpiritUNITE.Database.Repositories;

public sealed class ActivityRepository(DataOptions options) : IActivityRepository
{
    public async Task RecordLoginDayAsync(ulong gameUlongUID, DateOnly loginDay)
    {
        await using var database = new DataConnection(options);
        if (await database.GetTable<RoleLoginDay>()
            .AnyAsync(day => day.GameUlongUID == gameUlongUID && day.LoginDay == loginDay))
        {
            return;
        }

        try
        {
            await database.InsertAsync(new RoleLoginDay { GameUlongUID = gameUlongUID, LoginDay = loginDay });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
        }
    }

    public async Task<DateOnly[]> FindLoginDaysAsync(ulong gameUlongUID, DateOnly since)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleLoginDay>()
            .Where(day => day.GameUlongUID == gameUlongUID && day.LoginDay >= since)
            .Select(day => day.LoginDay)
            .ToArrayAsync();
    }

    public async Task<ActivityTaskReward[]> FindTaskRewardsAsync(ulong gameUlongUID, long periodStartSince)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleActivityTaskReward>()
            .Where(reward => reward.GameUlongUID == gameUlongUID && reward.PeriodStart >= periodStartSince)
            .Select(reward => new ActivityTaskReward(reward.TaskId, reward.PeriodStart, reward.RewardTime))
            .ToArrayAsync();
    }

    public async Task<bool> TryRecordTaskRewardAsync(ulong gameUlongUID, ActivityTaskReward reward)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(new RoleActivityTaskReward
            {
                GameUlongUID = gameUlongUID,
                TaskId = reward.TaskId,
                PeriodStart = reward.PeriodStart,
                RewardTime = reward.RewardTime,
            });
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task RemoveTaskRewardAsync(ulong gameUlongUID, ActivityTaskReward reward)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleActivityTaskReward>()
            .Where(saved => saved.GameUlongUID == gameUlongUID
                && saved.TaskId == reward.TaskId
                && saved.PeriodStart == reward.PeriodStart)
            .DeleteAsync();
    }
}
