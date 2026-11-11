using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record CreditRecord(int CreditPoint, long UnconfirmedDay, int UnconfirmedCount, long BanEndTime, long ResumeDay, int ResumeCredit);

public sealed record CreditReduceRecord(
    long GameStartTime,
    int MapMode,
    int MapSubMode,
    int Reason,
    int ReducePoint,
    int HeroID,
    string DeskUniqId);

public sealed record CreditPointTaskRecord(
    int TaskId,
    int Status,
    int TaskProcess,
    int CompleteNum,
    long RecvTime,
    long CompleteTime,
    long RewardTime);

public sealed class CreditRepository(DataOptions options)
{
    private const int TaskstatusComplete = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete;
    private const int TaskstatusRewarded = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded;

    public async Task<CreditPointTaskRecord[]> ListTasksAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await ListTasksAsync(database, gameUlongUID);
    }

    private static async Task<CreditPointTaskRecord[]> ListTasksAsync(DataConnection database, ulong gameUlongUID)
    {
        return await database.GetTable<RoleCreditTask>()
            .Where(task => task.GameUlongUID == gameUlongUID)
            .Select(task => new CreditPointTaskRecord(
                task.TaskId,
                task.Status,
                task.TaskProcess,
                task.CompleteNum,
                task.RecvTime,
                task.CompleteTime,
                task.RewardTime))
            .ToArrayAsync();
    }

    public async Task<T> ApplyTaskSettlementAsync<T>(SettlementKey key, Func<CreditPointTaskRecord[], (IReadOnlyList<CreditPointTaskRecord> Changed, T Result)> plan)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var outcome = await SettlementEffects.RunAsync(database, key, "credit.tasks", async () =>
        {
            var (changed, result) = plan(await ListTasksAsync(database, key.GameUlongUID));
            foreach (var task in changed)
            {
                await database.InsertOrReplaceAsync(ToEntity(key.GameUlongUID, task));
            }

            return result;
        });
        await transaction.CommitAsync();
        return outcome.Value;
    }

    public async Task SaveTasksAsync(ulong gameUlongUID, IReadOnlyList<CreditPointTaskRecord> tasks)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        foreach (var task in tasks)
        {
            await database.InsertOrReplaceAsync(ToEntity(gameUlongUID, task));
        }

        await transaction.CommitAsync();
    }

    public async Task<RoleWallet?> CommitTaskRewardAsync(ulong gameUlongUID, int taskId, long rewardTime, IReadOnlyDictionary<int, long> coins)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var claimed = await database.GetTable<RoleCreditTask>()
            .Where(task => task.GameUlongUID == gameUlongUID && task.TaskId == taskId && task.Status == TaskstatusComplete)
            .Set(task => task.Status, TaskstatusRewarded)
            .Set(task => task.RewardTime, rewardTime)
            .UpdateAsync();
        if (claimed == 0)
        {
            await transaction.RollbackAsync();
            return null;
        }

        foreach (var (coinID, amount) in coins)
        {
            await WalletRepository.CreditAsync(database, gameUlongUID, coinID, amount);
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return wallet;
    }

    private static RoleCreditTask ToEntity(ulong gameUlongUID, CreditPointTaskRecord task) => new()
    {
        GameUlongUID = gameUlongUID,
        TaskId = task.TaskId,
        Status = task.Status,
        TaskProcess = task.TaskProcess,
        CompleteNum = task.CompleteNum,
        RecvTime = task.RecvTime,
        CompleteTime = task.CompleteTime,
        RewardTime = task.RewardTime,
    };

    public async Task<CreditRecord?> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await FindAsync(database, gameUlongUID);
    }

    private static async Task<CreditRecord?> FindAsync(DataConnection database, ulong gameUlongUID)
    {
        var row = await database.GetTable<RoleCredit>().FirstOrDefaultAsync(credit => credit.GameUlongUID == gameUlongUID);
        return row is null
            ? null
            : new CreditRecord(row.CreditPoint, row.UnconfirmedDay, row.UnconfirmedCount, row.BanEndTime, row.ResumeDay, row.ResumeCredit);
    }

    public async Task SaveAsync(ulong gameUlongUID, CreditRecord record, CreditReduceRecord? reduceRecord, int maxReduceRecordNum)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await SaveAsync(database, gameUlongUID, record, reduceRecord, maxReduceRecordNum);
        await transaction.CommitAsync();
    }

    public async Task<T> ApplySettlementAsync<T>(
        SettlementKey key,
        string effect,
        CreditRecord starting,
        int maxReduceRecordNum,
        Func<CreditRecord, (CreditRecord Saved, CreditReduceRecord? Reduce, T Result)> change)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var outcome = await SettlementEffects.RunAsync(database, key, effect, async () =>
        {
            var record = await FindAsync(database, key.GameUlongUID) ?? starting;
            var (saved, reduce, result) = change(record);
            if (saved != record || reduce is not null)
            {
                await SaveAsync(database, key.GameUlongUID, saved, reduce, maxReduceRecordNum);
            }

            return result;
        });
        await transaction.CommitAsync();
        return outcome.Value;
    }

    public async Task<pbSCCreditPointNtf?> RecoverBattleAsync(ulong uid, SettlementKey? key, long day, int recovery, int dailyLimit, int maxCredit, int startingCredit)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        async Task<pbSCCreditPointNtf?> ApplyAsync()
        {
            var record = await FindAsync(database, uid) ?? new CreditRecord(startingCredit, 0, 0, 0, 0, 0);
            var savedDay = await database.GetTable<RoleCreditRecoveryDay>()
                .FirstOrDefaultAsync(row => row.GameUlongUID == uid && row.Day == day);
            var resumedToday = savedDay?.ResumeCredit ?? (record.ResumeDay == day ? record.ResumeCredit : 0);
            var allowed = Math.Clamp(dailyLimit - resumedToday, 0, recovery);
            var creditPoint = Math.Min(maxCredit, record.CreditPoint + allowed);
            var resumed = Math.Max(0, creditPoint - record.CreditPoint);
            await database.InsertOrReplaceAsync(new RoleCreditRecoveryDay
            {
                GameUlongUID = uid,
                Day = day,
                ResumeCredit = resumedToday + resumed,
            });
            if (resumed == 0)
            {
                return null;
            }

            var saved = record with { CreditPoint = creditPoint };
            if (day >= record.ResumeDay)
            {
                saved = saved with { ResumeDay = day, ResumeCredit = resumedToday + resumed };
            }

            await SaveAsync(database, uid, saved, null, 0);
            return new pbSCCreditPointNtf { CurrentCreditPoint = creditPoint };
        }

        if (key is null)
        {
            await SettlementEffects.LockRoleAsync(database, uid);
        }

        var result = key is null ? await ApplyAsync() : (await SettlementEffects.RunAsync(database, key, "credit.recovery", ApplyAsync)).Value;
        await transaction.CommitAsync();
        return result;
    }

    private static async Task SaveAsync(DataConnection database, ulong gameUlongUID, CreditRecord record, CreditReduceRecord? reduceRecord, int maxReduceRecordNum)
    {
        await database.InsertOrReplaceAsync(new RoleCredit
        {
            GameUlongUID = gameUlongUID,
            CreditPoint = record.CreditPoint,
            UnconfirmedDay = record.UnconfirmedDay,
            UnconfirmedCount = record.UnconfirmedCount,
            BanEndTime = record.BanEndTime,
            ResumeDay = record.ResumeDay,
            ResumeCredit = record.ResumeCredit,
        });
        if (reduceRecord is not null)
        {
            await AddReduceRecordAsync(database, gameUlongUID, reduceRecord, maxReduceRecordNum);
        }
    }

    public async Task<CreditReduceRecord[]> ListReduceRecordsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleCreditReduceRecord>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .OrderByDescending(saved => saved.CreditReduceRecordId)
            .Select(saved => new CreditReduceRecord(
                saved.GameStartTime,
                saved.MapMode,
                saved.MapSubMode,
                saved.Reason,
                saved.ReducePoint,
                saved.HeroID,
                saved.DeskUniqId))
            .ToArrayAsync();
    }

    public async Task<CreditReduceRecord[]> FindReduceRecordsOfDeskAsync(ulong gameUlongUID, string deskUniqId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleCreditReduceRecord>()
            .Where(saved => saved.GameUlongUID == gameUlongUID && saved.DeskUniqId == deskUniqId)
            .Select(saved => new CreditReduceRecord(
                saved.GameStartTime,
                saved.MapMode,
                saved.MapSubMode,
                saved.Reason,
                saved.ReducePoint,
                saved.HeroID,
                saved.DeskUniqId))
            .ToArrayAsync();
    }

    private static async Task AddReduceRecordAsync(DataConnection database, ulong gameUlongUID, CreditReduceRecord reduceRecord, int maxReduceRecordNum)
    {
        await database.InsertAsync(new RoleCreditReduceRecord
        {
            GameUlongUID = gameUlongUID,
            GameStartTime = reduceRecord.GameStartTime,
            MapMode = reduceRecord.MapMode,
            MapSubMode = reduceRecord.MapSubMode,
            Reason = reduceRecord.Reason,
            ReducePoint = reduceRecord.ReducePoint,
            HeroID = reduceRecord.HeroID,
            DeskUniqId = reduceRecord.DeskUniqId,
        });
        var overflow = await database.GetTable<RoleCreditReduceRecord>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .OrderByDescending(saved => saved.CreditReduceRecordId)
            .Skip(maxReduceRecordNum)
            .Select(saved => saved.CreditReduceRecordId)
            .ToArrayAsync();
        if (overflow.Length > 0)
        {
            await database.GetTable<RoleCreditReduceRecord>()
                .Where(saved => overflow.Contains(saved.CreditReduceRecordId))
                .DeleteAsync();
        }
    }
}
