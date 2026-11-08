using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record MegaEvolutionTask(int TaskType, int TaskID);

public sealed record MegaEvolutionState(
    int Complete,
    int CurStage,
    long LastDailyUpdateTime,
    long LastWeekUpdateTime,
    IReadOnlyList<MegaEvolutionTask> Tasks,
    IReadOnlySet<int> RewardedAwardKeys);

public sealed record MegaEvolutionTaskRefresh(
    long LastDailyUpdateTime,
    long LastWeekUpdateTime,
    IReadOnlyCollection<int> RefreshedTaskTypes,
    IReadOnlyList<MegaEvolutionTask> Tasks);

public sealed class MegaEvolutionRepository(DataOptions options)
{
    private const int OneRow = 1;
    private const int FirstStage = 1;

    public async Task<MegaEvolutionState> RefreshAsync(ulong gameUlongUID, int activityID, Func<MegaEvolutionState, MegaEvolutionTaskRefresh?> planRefresh)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await EnsureRowAsync(database, gameUlongUID, activityID);
        await LockAsync(database, gameUlongUID, activityID);
        var state = await ReadAsync(database, gameUlongUID, activityID);
        if (planRefresh(state) is { } refresh)
        {
            await database.GetTable<RoleMegaEvolutionTask>()
                .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID && refresh.RefreshedTaskTypes.Contains(row.TaskType))
                .DeleteAsync();
            foreach (var task in refresh.Tasks)
            {
                await database.InsertAsync(new RoleMegaEvolutionTask
                {
                    GameUlongUID = gameUlongUID,
                    ActivityID = activityID,
                    TaskType = task.TaskType,
                    TaskID = task.TaskID,
                });
            }

            await database.GetTable<RoleMegaEvolution>()
                .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID)
                .Set(row => row.LastDailyUpdateTime, refresh.LastDailyUpdateTime)
                .Set(row => row.LastWeekUpdateTime, refresh.LastWeekUpdateTime)
                .UpdateAsync();
            state = await ReadAsync(database, gameUlongUID, activityID);
        }

        await transaction.CommitAsync();
        return state;
    }

    public async Task<ActivityRewardCommit> CommitTrainAsync(ulong gameUlongUID, int activityID, int completeBefore, int completeAfter, BagChange cost, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var trained = await database.GetTable<RoleMegaEvolution>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID && row.Complete == completeBefore)
            .Set(row => row.Complete, completeAfter)
            .UpdateAsync() == OneRow;
        if (!trained)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        return await FinishAsync(database, transaction, gameUlongUID, cost, now);
    }

    public async Task<ActivityRewardCommit> CommitTaskFinishAsync(ulong gameUlongUID, ActivityTaskFinish finish, BagChange cost)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await ActivityRepository.TryInsertTaskFinishAsync(database, gameUlongUID, finish))
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        return await FinishAsync(database, transaction, gameUlongUID, cost, finish.FinishTime);
    }

    public async Task<ActivityRewardCommit> CommitRewardAsync(ulong gameUlongUID, int activityID, IReadOnlyCollection<int> awardKeys, BagChange grants, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            foreach (var awardKey in awardKeys)
            {
                await database.InsertAsync(new RoleMegaEvolutionReward
                {
                    GameUlongUID = gameUlongUID,
                    ActivityID = activityID,
                    AwardKey = awardKey,
                    RewardTime = now,
                });
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionRewardHasReceived);
        }

        return await FinishAsync(database, transaction, gameUlongUID, grants, now);
    }

    public async Task<bool> EnterNextStageAsync(ulong gameUlongUID, int activityID, int curStage, int completeBefore)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleMegaEvolution>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID && row.CurStage == curStage && row.Complete == completeBefore)
            .Set(row => row.CurStage, curStage + 1)
            .Set(row => row.Complete, 0)
            .UpdateAsync() == OneRow;
    }

    private static async Task<ActivityRewardCommit> FinishAsync(
        DataConnection database,
        DataConnectionTransaction transaction,
        ulong gameUlongUID,
        BagChange change,
        long now)
    {
        var commit = await ActivityRepository.ApplyRewardChangeAsync(database, gameUlongUID, change, now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return commit;
        }

        await transaction.CommitAsync();
        return commit;
    }

    private static async Task<MegaEvolutionState> ReadAsync(DataConnection database, ulong gameUlongUID, int activityID)
    {
        var row = await database.GetTable<RoleMegaEvolution>()
            .FirstAsync(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID);
        var tasks = await database.GetTable<RoleMegaEvolutionTask>()
            .Where(task => task.GameUlongUID == gameUlongUID && task.ActivityID == activityID)
            .OrderBy(task => task.TaskType)
            .ThenBy(task => task.TaskID)
            .Select(task => new MegaEvolutionTask(task.TaskType, task.TaskID))
            .ToArrayAsync();
        var rewarded = await database.GetTable<RoleMegaEvolutionReward>()
            .Where(reward => reward.GameUlongUID == gameUlongUID && reward.ActivityID == activityID)
            .Select(reward => reward.AwardKey)
            .ToArrayAsync();
        return new MegaEvolutionState(row.Complete, row.CurStage, row.LastDailyUpdateTime, row.LastWeekUpdateTime, tasks, rewarded.ToHashSet());
    }

    private static Task LockAsync(DataConnection database, ulong gameUlongUID, int activityID) =>
        database.GetTable<RoleMegaEvolution>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID)
            .Set(row => row.CurStage, row => row.CurStage)
            .UpdateAsync();

    private static async Task EnsureRowAsync(DataConnection database, ulong gameUlongUID, int activityID)
    {
        if (await database.GetTable<RoleMegaEvolution>().AnyAsync(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID))
        {
            return;
        }

        try
        {
            await database.InsertAsync(new RoleMegaEvolution { GameUlongUID = gameUlongUID, ActivityID = activityID, CurStage = FirstStage });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
        }
    }
}
