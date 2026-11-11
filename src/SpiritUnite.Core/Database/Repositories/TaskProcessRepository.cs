using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed class TaskProcessRepository(DataOptions options)
{
    private const int OneRow = 1;
    private const long NotCompleted = 0;

    public async Task<IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess>> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleTaskProcess>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        return rows
            .GroupBy(row => (row.TaskId, row.PeriodStart))
            .ToDictionary(group => group.Key, ToStoredTaskProcess);
    }

    public async Task<bool> AddAsync(ulong gameUlongUID, IReadOnlyCollection<TaskProcessIncrement> increments, long now)
    {
        if (increments.Count == 0)
        {
            return true;
        }

        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return false;
        }

        await ApplyAsync(database, gameUlongUID, increments, now);
        await transaction.CommitAsync();
        return true;
    }

    public async Task<pbCOMDT_TASK_INFO[]> ApplySettlementAsync(SettlementKey key, SettlementTaskPlan plan)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var effect = await SettlementEffects.RunAsync(database, key, plan.Effect, async () =>
        {
            if (!await LockRoleAsync(database, key.GameUlongUID))
            {
                throw new InvalidDataException($"Settlement role {key.GameUlongUID} is missing");
            }

            await ApplyAsync(database, key.GameUlongUID, plan.Increments, plan.Now);
            return await ReadSettlementTasksAsync(database, key.GameUlongUID, plan);
        });
        await transaction.CommitAsync();
        return effect.Value;
    }

    internal static async Task<pbCOMDT_TASK_INFO[]> ReadSettlementTasksAsync(DataConnection database, ulong uid, SettlementTaskPlan plan)
    {
        var taskIds = plan.Tasks.Select(task => task.Info.TaskId).ToArray();
        var rows = await database.GetTable<RoleTaskProcess>()
            .Where(row => row.GameUlongUID == uid && taskIds.Contains(row.TaskId))
            .ToArrayAsync();
        var stored = rows.GroupBy(row => (row.TaskId, row.PeriodStart)).ToDictionary(group => group.Key, ToStoredTaskProcess);
        return plan.Tasks.Select(task => BuildSettlementTask(task, stored)).ToArray();
    }

    private static pbCOMDT_TASK_INFO BuildSettlementTask(
        SettlementTaskTemplate task,
        IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess> stored)
    {
        var info = task.Info.Clone();
        var process = stored.GetValueOrDefault((info.TaskId, task.PeriodStart));
        for (var index = 0; index < task.StoredConditionIndexes.Length; index++)
        {
            if (task.StoredConditionIndexes[index] >= 0)
            {
                info.TaskProcess[index] = Math.Min(task.Targets[index], process?.ProcessOf(task.StoredConditionIndexes[index]) ?? 0);
            }
        }

        var completed = info.IsLockProcess == 0 && task.Targets.Length > 0
            && task.Targets.Select((target, index) => info.TaskProcess[index] >= target).All(value => value);
        if (info.Status != (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded)
        {
            info.Status = (int)(completed ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved);
        }

        var received = info.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved;
        info.CompleteNum = received ? 0 : 1;
        info.CompleteTime = received ? 0 : task.CompleteTime switch
        {
            SettlementTaskCompleteTime.Received => info.RecvTime,
            SettlementTaskCompleteTime.StoredAfterReceived => Math.Max(info.RecvTime, Math.Max(info.CompleteTime, process?.CompleteTime ?? 0)),
            SettlementTaskCompleteTime.Reward => info.RewardTime,
            _ => process is { CompleteTime: > 0 } ? process.CompleteTime : task.PeriodStart,
        };
        if (task.HideNewTagOnComplete && !received)
        {
            info.NewTag = 0;
        }

        return info;
    }

    internal static async Task ApplyAsync(
        DataConnection database,
        ulong gameUlongUID,
        IReadOnlyCollection<TaskProcessIncrement> increments,
        long now)
    {
        var taskIds = increments.Select(increment => increment.TaskId).Distinct().ToArray();
        var stored = (await database.GetTable<RoleTaskProcess>()
                .Where(row => row.GameUlongUID == gameUlongUID && taskIds.Contains(row.TaskId))
                .ToArrayAsync())
            .ToDictionary(row => (row.TaskId, row.PeriodStart, row.ConditionIndex));
        foreach (var increment in increments)
        {
            if (increment.CountedValue is { } countedValue && !await CountValueOnceAsync(database, gameUlongUID, increment, countedValue))
            {
                continue;
            }

            if (stored.TryGetValue((increment.TaskId, increment.PeriodStart, increment.ConditionIndex), out var row))
            {
                var taskProcess = BattleTasks.AddCounts(row.TaskProcess, increment.Delta);
                var completeTime = FindCompleteTime(row.CompleteTime, taskProcess, increment, now);
                await database.GetTable<RoleTaskProcess>()
                    .Where(process => process.GameUlongUID == gameUlongUID
                        && process.TaskId == increment.TaskId
                        && process.PeriodStart == increment.PeriodStart
                        && process.ConditionIndex == increment.ConditionIndex)
                    .Set(process => process.TaskProcess, taskProcess)
                    .Set(process => process.CompleteTime, completeTime)
                    .UpdateAsync();
                row.TaskProcess = taskProcess;
                row.CompleteTime = completeTime;
                continue;
            }

            var firstProcess = BattleTasks.AddCounts(0, increment.Delta);
            var inserted = new RoleTaskProcess
            {
                GameUlongUID = gameUlongUID,
                TaskId = increment.TaskId,
                PeriodStart = increment.PeriodStart,
                ConditionIndex = increment.ConditionIndex,
                TaskProcess = firstProcess,
                CompleteTime = FindCompleteTime(NotCompleted, firstProcess, increment, now),
            };
            await database.InsertAsync(inserted);
            stored[(inserted.TaskId, inserted.PeriodStart, inserted.ConditionIndex)] = inserted;
        }
    }

    private static async Task<bool> CountValueOnceAsync(DataConnection database, ulong gameUlongUID, TaskProcessIncrement increment, int countedValue)
    {
        var counted = await database.GetTable<RoleTaskProcessValue>()
            .AnyAsync(value => value.GameUlongUID == gameUlongUID
                && value.TaskId == increment.TaskId
                && value.PeriodStart == increment.PeriodStart
                && value.ConditionIndex == increment.ConditionIndex
                && value.CountedValue == countedValue);
        if (counted)
        {
            return false;
        }

        await database.InsertAsync(new RoleTaskProcessValue
        {
            GameUlongUID = gameUlongUID,
            TaskId = increment.TaskId,
            PeriodStart = increment.PeriodStart,
            ConditionIndex = increment.ConditionIndex,
            CountedValue = countedValue,
        });
        return true;
    }

    private static long FindCompleteTime(long storedCompleteTime, int taskProcess, TaskProcessIncrement increment, long now) =>
        storedCompleteTime != NotCompleted
            ? storedCompleteTime
            : taskProcess >= increment.TargetProcess ? now : NotCompleted;

    internal static async Task<bool> LockRoleAsync(DataConnection database, ulong gameUlongUID) =>
        (await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .Select(role => role.GameUlongUID)
            .ToArrayAsync()).Length == OneRow;

    private static StoredTaskProcess ToStoredTaskProcess(IEnumerable<RoleTaskProcess> conditionRows)
    {
        var rows = conditionRows.ToArray();
        var first = rows.FirstOrDefault(row => row.ConditionIndex == StoredTaskProcess.FirstCondition);
        var completeTime = rows.All(row => row.CompleteTime != NotCompleted) ? rows.Max(row => row.CompleteTime) : NotCompleted;
        return new StoredTaskProcess(rows[0].TaskId, rows[0].PeriodStart, first?.TaskProcess ?? 0, completeTime)
        {
            ConditionProcesses = rows.ToDictionary(row => row.ConditionIndex, row => row.TaskProcess),
        };
    }
}
