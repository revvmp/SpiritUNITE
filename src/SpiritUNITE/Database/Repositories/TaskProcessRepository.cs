using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Tasks;

namespace SpiritUNITE.Database.Repositories;

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
        return rows.Select(ToStoredTaskProcess).ToDictionary(process => (process.TaskId, process.PeriodStart));
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
            .ToDictionary(row => (row.TaskId, row.PeriodStart));
        foreach (var increment in increments)
        {
            if (stored.TryGetValue((increment.TaskId, increment.PeriodStart), out var row))
            {
                var taskProcess = BattleTasks.AddCounts(row.TaskProcess, increment.Delta);
                await database.GetTable<RoleTaskProcess>()
                    .Where(process => process.GameUlongUID == gameUlongUID
                        && process.TaskId == increment.TaskId
                        && process.PeriodStart == increment.PeriodStart)
                    .Set(process => process.TaskProcess, taskProcess)
                    .Set(process => process.CompleteTime, FindCompleteTime(row.CompleteTime, taskProcess, increment, now))
                    .UpdateAsync();
                continue;
            }

            var firstProcess = BattleTasks.AddCounts(0, increment.Delta);
            await database.InsertAsync(new RoleTaskProcess
            {
                GameUlongUID = gameUlongUID,
                TaskId = increment.TaskId,
                PeriodStart = increment.PeriodStart,
                TaskProcess = firstProcess,
                CompleteTime = FindCompleteTime(NotCompleted, firstProcess, increment, now),
            });
        }
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

    private static StoredTaskProcess ToStoredTaskProcess(RoleTaskProcess row) =>
        new(row.TaskId, row.PeriodStart, row.TaskProcess, row.CompleteTime);
}
