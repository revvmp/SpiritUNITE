using System.Globalization;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record ActivityRewardCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    IReadOnlyDictionary<int, long> SpecialCoinsBefore,
    RoleWallet? Wallet,
    IReadOnlyList<BagItem> ChangedItems)
{
    public static ActivityRewardCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, new Dictionary<int, long>(), null, []);
}

public sealed record ActivityRewardChange(
    IReadOnlyList<RoleItemInfo> Items,
    IReadOnlyList<int> PokemonIds,
    BagChange Change,
    IReadOnlyList<TaskProcessIncrement> ProcessIncrements);

public sealed class ActivityRepository(DataOptions options)
{
    public async Task<ActivitySettlementReward> CommitSettlementRewardAsync(
        SettlementKey key,
        ActivityTaskReward reward,
        ActivityTaskGrant grant,
        ActivityRewardChange change,
        SettlementTaskPlan progress)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var receipt = await SettlementEffects.RunAsync(database, key, SettlementRewardEffect(reward.TaskId, reward.PeriodStart), async () =>
        {
            var alreadyClaimed = await database.GetTable<RoleActivityTaskReward>()
                .AnyAsync(row => row.GameUlongUID == key.GameUlongUID && row.TaskId == reward.TaskId && row.PeriodStart == reward.PeriodStart);
            var commit = alreadyClaimed
                ? ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked)
                : await ApplyTaskRewardAsync(database, key.GameUlongUID, reward, change);
            if (!alreadyClaimed && commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                throw new InvalidDataException($"Activity settlement task {reward.TaskId} reward failed: {commit.Result}");
            }

            var tasks = await TaskProcessRepository.ReadSettlementTasksAsync(database, key.GameUlongUID, progress);
            var rewards = await database.GetTable<RoleActivityTaskReward>()
                .Where(row => row.GameUlongUID == key.GameUlongUID)
                .ToArrayAsync();
            foreach (var task in tasks)
            {
                var period = progress.Tasks.First(template => template.Info.TaskId == task.TaskId).PeriodStart;
                if (rewards.FirstOrDefault(row => row.TaskId == task.TaskId && row.PeriodStart == period) is { } claimed)
                {
                    task.Status = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded;
                    task.CompleteNum = 1;
                    task.RewardTime = claimed.RewardTime;
                }
            }

            return new ActivitySettlementReward(commit.Wallet is null ? null : new ActivityTaskGrantResult(grant, commit), tasks);
        });
        await transaction.CommitAsync();
        return receipt.Value;
    }

    public async Task<Dictionary<(int TaskId, long PeriodStart), ActivitySettlementReward>> FindSettlementRewardsAsync(SettlementKey key)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<SettlementEffectRecord>()
            .Where(row => row.SettlementID == key.SettlementID && row.GameUlongUID == key.GameUlongUID && row.Effect.StartsWith(SettlementRewardEffectPrefix))
            .ToArrayAsync();
        return rows.ToDictionary(row => ParseSettlementRewardEffect(row.Effect), row => SettlementEffects.Deserialize<ActivitySettlementReward>(row.Output));
    }

    private const string SettlementRewardEffectPrefix = "activity.reward.";

    private static string SettlementRewardEffect(int taskId, long periodStart) => $"{SettlementRewardEffectPrefix}{taskId}.{periodStart}";

    private static (int TaskId, long PeriodStart) ParseSettlementRewardEffect(string effect)
    {
        var parts = effect[SettlementRewardEffectPrefix.Length..].Split('.');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), long.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    public async Task<bool> RecordLoginDayAsync(ulong gameUlongUID, DateOnly loginDay)
    {
        await using var database = new DataConnection(options);
        if (await database.GetTable<RoleLoginDay>()
            .AnyAsync(day => day.GameUlongUID == gameUlongUID && day.LoginDay == loginDay))
        {
            return false;
        }

        try
        {
            await database.InsertAsync(new RoleLoginDay { GameUlongUID = gameUlongUID, LoginDay = loginDay });
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
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

    public async Task<ActivityTaskFinish[]> FindTaskFinishesAsync(ulong gameUlongUID, long periodStartSince)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleActivityTaskFinish>()
            .Where(finish => finish.GameUlongUID == gameUlongUID && finish.PeriodStart >= periodStartSince)
            .Select(finish => new ActivityTaskFinish(finish.TaskId, finish.PeriodStart, finish.FinishTime))
            .ToArrayAsync();
    }

    internal static async Task<bool> TryInsertTaskFinishAsync(DataConnection database, ulong gameUlongUID, ActivityTaskFinish finish)
    {
        try
        {
            await database.InsertAsync(new RoleActivityTaskFinish
            {
                GameUlongUID = gameUlongUID,
                TaskId = finish.TaskId,
                PeriodStart = finish.PeriodStart,
                FinishTime = finish.FinishTime,
            });
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    internal static async Task<ActivityRewardCommit> ApplyRewardChangeAsync(DataConnection database, ulong gameUlongUID, BagChange change, long now)
    {
        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var coinID in change.CoinGrants.Keys.Concat(change.CoinCosts.Keys).Distinct().Where(CoinID.IsSpecialCoin))
        {
            specialCoinsBefore[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, change, now);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return ActivityRewardCommit.Refused(applied);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        return new ActivityRewardCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, specialCoinsBefore, wallet, changedItems);
    }

    public async Task<bool> CommitUnclaimedTaskMailAsync(ulong gameUlongUID, IReadOnlyList<ActivityTaskReward> rewards, IReadOnlyList<RoleMail> mails)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            foreach (var reward in rewards)
            {
                await database.InsertAsync(new RoleActivityTaskReward
                {
                    GameUlongUID = gameUlongUID,
                    TaskId = reward.TaskId,
                    PeriodStart = reward.PeriodStart,
                    RewardTime = reward.RewardTime,
                });
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return false;
        }

        foreach (var mail in mails)
        {
            await MailRepository.InsertAsync(database, mail);
        }

        await transaction.CommitAsync();
        return true;
    }

    public async Task<ActivityRewardCommit> CommitTaskRewardAsync(
        ulong gameUlongUID,
        ActivityTaskReward reward,
        ActivityRewardChange rewardChange)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var result = await ApplyTaskRewardAsync(database, gameUlongUID, reward, rewardChange);
        if (result.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }
        return result;
    }

    private static async Task<ActivityRewardCommit> ApplyTaskRewardAsync(
        DataConnection database,
        ulong gameUlongUID,
        ActivityTaskReward reward,
        ActivityRewardChange rewardChange)
    {
        if (!await TaskProcessRepository.LockRoleAsync(database, gameUlongUID))
        {
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        try
        {
            await database.InsertAsync(new RoleActivityTaskReward
            {
                GameUlongUID = gameUlongUID,
                TaskId = reward.TaskId,
                PeriodStart = reward.PeriodStart,
                RewardTime = reward.RewardTime,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        }

        var commit = await ApplyRewardChangeAsync(
            database,
            gameUlongUID,
            rewardChange,
            reward.RewardTime,
            PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTaskaward,
            PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        if (commit.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await TaskProcessRepository.ApplyAsync(database, gameUlongUID, rewardChange.ProcessIncrements, reward.RewardTime);
        }

        return commit;
    }

    public static async Task<ActivityRewardCommit> ApplyRewardChangeAsync(
        DataConnection database,
        ulong gameUlongUID,
        ActivityRewardChange rewardChange,
        long rewardTime,
        PB_ENUM_ENM_RESOURCE_CHG_REASON pokemonGainReason,
        PB_ENUM_RES_ERROR_CODE ownedItemResult)
    {
        try
        {
            foreach (var item in rewardChange.Items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return ActivityRewardCommit.Refused(ownedItemResult);
        }

        try
        {
            foreach (var pokemonId in rewardChange.PokemonIds)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, new RolePokemonInfo(
                    pokemonId,
                    rewardTime,
                    (int)pokemonGainReason,
                    RolePokemonInfo.HasNewTag)));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var coinID in rewardChange.Change.CoinGrants.Keys.Where(CoinID.IsSpecialCoin))
        {
            specialCoinsBefore[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, rewardChange.Change, rewardTime);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return ActivityRewardCommit.Refused(applied);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, rewardChange.Change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        return new ActivityRewardCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, specialCoinsBefore, wallet, changedItems);
    }
}
