using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record ActivityRewardCommit(PB_ENUM_RES_ERROR_CODE Result, RoleWallet? Wallet, IReadOnlyList<BagItem> ChangedItems)
{
    public static ActivityRewardCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, []);
}

public sealed class ActivityRepository(DataOptions options)
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

    public async Task<ActivityRewardCommit> CommitTaskRewardAsync(
        ulong gameUlongUID,
        ActivityTaskReward reward,
        IReadOnlyList<RoleItemInfo> items,
        BagChange change)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(new RoleActivityTaskReward
            {
                GameUlongUID = gameUlongUID,
                TaskId = reward.TaskId,
                PeriodStart = reward.PeriodStart,
                RewardTime = reward.RewardTime,
            });
            foreach (var item in items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, change, reward.RewardTime);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(applied);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new ActivityRewardCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet, changedItems);
    }
}
