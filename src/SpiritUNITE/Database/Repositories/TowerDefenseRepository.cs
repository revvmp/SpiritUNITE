using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record TowerDefenseBuff(int BuffID, int BuffLevel, int AttrID, int IsNewUnLock);

public sealed record TowerDefenseState(
    int Score,
    int MaxPassGameLevel,
    int IsNewUnLock,
    IReadOnlyDictionary<int, int> ShopBuyNums,
    IReadOnlyList<TowerDefenseBuff> Buffs);

public sealed record TowerDefenseShopBuy(int ShopID, int ShopCnt, int BuyNumLimit, int ScoreCost, TowerDefenseBuff? Buff);

public sealed class TowerDefenseRepository(DataOptions options)
{
    private const int OneRow = 1;
    private const int NewUnLock = 1;
    private const int NoNewUnLock = 0;

    public async Task<TowerDefenseState> FindAsync(ulong gameUlongUID, int periodID)
    {
        await using var database = new DataConnection(options);
        return await ReadAsync(database, gameUlongUID, periodID);
    }

    public async Task<ActivityRewardCommit> CommitShopBuyAsync(
        ulong gameUlongUID,
        int periodID,
        TowerDefenseShopBuy buy,
        BagChange grants,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TrySpendScoreAsync(database, gameUlongUID, periodID, buy.ScoreCost))
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseShopBuyScoreLimit);
        }

        if (!await TryAddShopBuyAsync(database, gameUlongUID, periodID, buy))
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseShopBuyNumLimit);
        }

        if (buy.Buff is { } buff)
        {
            await SetBuffAsync(database, gameUlongUID, periodID, buff);
        }

        var commit = await ActivityRepository.ApplyRewardChangeAsync(database, gameUlongUID, grants, now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return commit;
        }

        await transaction.CommitAsync();
        return commit;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> LevelUpBuffAsync(ulong gameUlongUID, int periodID, TowerDefenseBuff from, TowerDefenseBuff to, int scoreCost)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TrySpendScoreAsync(database, gameUlongUID, periodID, scoreCost))
        {
            await transaction.RollbackAsync();
            return PB_ENUM_RES_ERROR_CODE.PbErrTowerDefenseLevelUpScoreLimit;
        }

        var raised = await database.GetTable<RoleTowerDefenseBuff>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodID == periodID && row.BuffID == from.BuffID && row.BuffLevel == from.BuffLevel)
            .Set(row => row.BuffLevel, to.BuffLevel)
            .Set(row => row.AttrID, to.AttrID)
            .UpdateAsync() == OneRow;
        if (!raised)
        {
            await transaction.RollbackAsync();
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await transaction.CommitAsync();
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task ClearBuffNewTagAsync(ulong gameUlongUID, int periodID)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleTowerDefenseBuff>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodID == periodID && row.IsNewUnLock != NoNewUnLock)
            .Set(row => row.IsNewUnLock, NoNewUnLock)
            .UpdateAsync();
    }

    private static async Task<bool> TrySpendScoreAsync(DataConnection database, ulong gameUlongUID, int periodID, int scoreCost) =>
        scoreCost == 0 || await database.GetTable<RoleTowerDefense>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodID == periodID && row.Score >= scoreCost)
            .Set(row => row.Score, row => row.Score - scoreCost)
            .UpdateAsync() == OneRow;

    private static async Task<bool> TryAddShopBuyAsync(DataConnection database, ulong gameUlongUID, int periodID, TowerDefenseShopBuy buy)
    {
        var added = await database.GetTable<RoleTowerDefenseShop>()
            .Where(row => row.GameUlongUID == gameUlongUID
                && row.PeriodID == periodID
                && row.ShopID == buy.ShopID
                && row.ShopBuyNum + buy.ShopCnt <= buy.BuyNumLimit)
            .Set(row => row.ShopBuyNum, row => row.ShopBuyNum + buy.ShopCnt)
            .UpdateAsync() == OneRow;
        if (added)
        {
            return true;
        }

        try
        {
            await database.InsertAsync(new RoleTowerDefenseShop
            {
                GameUlongUID = gameUlongUID,
                PeriodID = periodID,
                ShopID = buy.ShopID,
                ShopBuyNum = buy.ShopCnt,
            });
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    private static async Task SetBuffAsync(DataConnection database, ulong gameUlongUID, int periodID, TowerDefenseBuff buff)
    {
        var updated = await database.GetTable<RoleTowerDefenseBuff>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodID == periodID && row.BuffID == buff.BuffID)
            .Set(row => row.BuffLevel, buff.BuffLevel)
            .Set(row => row.AttrID, buff.AttrID)
            .Set(row => row.IsNewUnLock, NewUnLock)
            .UpdateAsync();
        if (updated == 0)
        {
            await database.InsertAsync(new RoleTowerDefenseBuff
            {
                GameUlongUID = gameUlongUID,
                PeriodID = periodID,
                BuffID = buff.BuffID,
                BuffLevel = buff.BuffLevel,
                AttrID = buff.AttrID,
                IsNewUnLock = NewUnLock,
            });
        }
    }

    private static async Task<TowerDefenseState> ReadAsync(DataConnection database, ulong gameUlongUID, int periodID)
    {
        var towerDefense = await database.GetTable<RoleTowerDefense>()
            .FirstOrDefaultAsync(row => row.GameUlongUID == gameUlongUID && row.PeriodID == periodID);
        var shops = await database.GetTable<RoleTowerDefenseShop>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodID == periodID)
            .ToDictionaryAsync(row => row.ShopID, row => row.ShopBuyNum);
        var buffs = await database.GetTable<RoleTowerDefenseBuff>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodID == periodID)
            .OrderBy(row => row.BuffID)
            .Select(row => new TowerDefenseBuff(row.BuffID, row.BuffLevel, row.AttrID, row.IsNewUnLock))
            .ToArrayAsync();
        return new TowerDefenseState(
            towerDefense?.Score ?? 0,
            towerDefense?.MaxPassGameLevel ?? 0,
            towerDefense?.IsNewUnLock ?? 0,
            shops,
            buffs);
    }
}
