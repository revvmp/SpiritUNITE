using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record NewbieChallengeRewardCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    RoleWallet? Wallet,
    IReadOnlyList<BagItem> ChangedItems,
    IReadOnlyDictionary<int, long> BeforeCoinNums)
{
    public static NewbieChallengeRewardCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, [], new Dictionary<int, long>());
}

public sealed class NewbieChallengeRepository(DataOptions options)
{
    public async Task<long> ReceiveAsync(ulong gameUlongUID, long now)
    {
        await using var database = new DataConnection(options);
        if (await FindRecvTimeAsync(database, gameUlongUID) is { } recvTime)
        {
            return recvTime;
        }

        try
        {
            await database.InsertAsync(new RoleNewbieChallenge { GameUlongUID = gameUlongUID, RecvTime = now });
            return now;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return await FindRecvTimeAsync(database, gameUlongUID) ?? now;
        }
    }

    public async Task<IReadOnlyDictionary<int, long>> FindRewardTimesAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleNewbieChallengeTask>()
            .Where(task => task.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(task => task.TaskId, task => task.RewardTime);
    }

    public async Task<NewbieChallengeRewardCommit> CommitTaskRewardAsync(ulong gameUlongUID, int taskId, long rewardTime, BagChange change)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(new RoleNewbieChallengeTask { GameUlongUID = gameUlongUID, TaskId = taskId, RewardTime = rewardTime });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return NewbieChallengeRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeRewarded);
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var coinID in change.CoinGrants.Keys)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, change, rewardTime);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return NewbieChallengeRewardCommit.Refused(applied);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new NewbieChallengeRewardCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet, changedItems, beforeCoinNums);
    }

    private static async Task<long?> FindRecvTimeAsync(DataConnection database, ulong gameUlongUID) =>
        await database.GetTable<RoleNewbieChallenge>()
            .Where(challenge => challenge.GameUlongUID == gameUlongUID)
            .Select(challenge => (long?)challenge.RecvTime)
            .FirstOrDefaultAsync();
}
