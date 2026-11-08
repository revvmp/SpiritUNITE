using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record ActivityExchangeTrade(
    int ExchangeID,
    int DoExchangeNum,
    int MaxDoneNum,
    BagChange Change,
    PveChallengeBuff? Buff,
    CouponsFlowEntry? CouponsFlow,
    long Now);

public sealed record ActivityExchangeCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    int DoneCount,
    IReadOnlyList<BagItem> ChangedItems,
    RoleWallet? Wallet,
    IReadOnlyDictionary<int, long> BeforeCoinNums,
    PveChallengeShopState? BuffState)
{
    public static ActivityExchangeCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, 0, [], null, new Dictionary<int, long>(), null);
}

public sealed class ActivityExchangeRepository(DataOptions options)
{
    public async Task<IReadOnlyDictionary<int, int>> LoadDoneCountsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleActivityExchange>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(row => row.ExchangeID, row => row.DoneCount);
    }

    public async Task<ActivityExchangeCommit> CommitAsync(ulong gameUlongUID, ActivityExchangeTrade trade)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TaskProcessRepository.LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return ActivityExchangeCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeAddDoneCntFail);
        }

        var doneCount = await database.GetTable<RoleActivityExchange>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ExchangeID == trade.ExchangeID)
            .Select(row => row.DoneCount)
            .FirstOrDefaultAsync();
        if ((long)doneCount + trade.DoExchangeNum > trade.MaxDoneNum)
        {
            await transaction.RollbackAsync();
            return ActivityExchangeCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrSpecialActivityOverLimit);
        }

        if (trade.Buff is { } buff && !await PveChallengeRepository.IsNextBuffLevelAsync(database, gameUlongUID, buff))
        {
            await transaction.RollbackAsync();
            return ActivityExchangeCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeCheckFail);
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var coinID in trade.Change.CoinCosts.Keys.Concat(trade.Change.CoinGrants.Keys).Distinct())
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, trade.Change, trade.Now);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return ActivityExchangeCommit.Refused(applied);
        }

        var newDoneCount = doneCount + trade.DoExchangeNum;
        await database.GetTable<RoleActivityExchange>().InsertOrUpdateAsync(
            () => new RoleActivityExchange { GameUlongUID = gameUlongUID, ExchangeID = trade.ExchangeID, DoneCount = newDoneCount },
            _ => new RoleActivityExchange { DoneCount = newDoneCount });
        if (trade.Buff is { } newBuff)
        {
            await PveChallengeRepository.SaveNewBuffLevelAsync(database, gameUlongUID, newBuff);
        }

        if (trade.CouponsFlow is { } couponsFlow)
        {
            await CouponsFlowRepository.AddAsync(database, gameUlongUID, couponsFlow);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, trade.Change.TouchedItems);
        var wallet = trade.Change.ChangesCoins ? await WalletRepository.ReadAsync(database, gameUlongUID) : null;
        var buffState = trade.Buff is not null ? await PveChallengeRepository.ReadShopStateAsync(database, gameUlongUID) : null;
        await transaction.CommitAsync();
        return new ActivityExchangeCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, newDoneCount, changedItems, wallet, beforeCoinNums, buffState);
    }
}
