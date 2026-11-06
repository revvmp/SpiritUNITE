using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

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
        ActivityRewardChange rewardChange)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (rewardChange.ProcessIncrements.Count > 0 && !await TaskProcessRepository.LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
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
            foreach (var item in rewardChange.Items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        }

        try
        {
            foreach (var pokemonId in rewardChange.PokemonIds)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, new RolePokemonInfo(
                    pokemonId,
                    reward.RewardTime,
                    (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTaskaward,
                    RolePokemonInfo.HasNewTag)));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var coinID in rewardChange.Change.CoinGrants.Keys.Where(CoinID.IsSpecialCoin))
        {
            specialCoinsBefore[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, rewardChange.Change, reward.RewardTime);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(applied);
        }

        await TaskProcessRepository.ApplyAsync(database, gameUlongUID, rewardChange.ProcessIncrements, reward.RewardTime);
        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, rewardChange.Change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new ActivityRewardCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, specialCoinsBefore, wallet, changedItems);
    }
}
