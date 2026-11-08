using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record PveChallengeWeek(long WeekStartTime, int WeekRewardGetNum, int PrivilegeRewardGetNum);

public sealed record PveChallengeBuff(int BuffID, int BuffLevel, int IsNewUnLock);

public sealed record PveChallengeShopBuy(int ShopID, int ShopBuyNum);

public sealed record PveChallengeShopState(IReadOnlyList<PveChallengeBuff> Buffs, IReadOnlyList<PveChallengeShopBuy> ShopBuys);

public sealed record PveChallengeShopPurchase(
    int ShopID,
    int ShopCnt,
    int BuyNumLimit,
    PveChallengeBuff? Buff,
    int CoinID,
    long CoinCost);

public sealed record PveChallengeShopCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    long BeforeCoinNum,
    RoleWallet? Wallet,
    PveChallengeShopState? State)
{
    public static PveChallengeShopCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, 0, null, null);
}

public sealed record PvePrivilegeActivation(int ActivityID, int CostCoupons, long PrivilegeExpireTime, long Now, CouponsFlowEntry? CouponsFlow);

public sealed record PvePrivilegeCommit(PB_ENUM_RES_ERROR_CODE Result, RoleWallet? Wallet);

public sealed class PveChallengeRepository(DataOptions options)
{
    private const int OneRow = 1;
    private const int NoBuffLevel = 0;
    private const int NewUnLock = 1;
    private const int NotNewUnLock = 0;

    public async Task<PveChallengeShopState> LoadShopStateAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await ReadShopStateAsync(database, gameUlongUID);
    }

    public async Task<PveChallengeWeek?> LoadWeekAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RolePveChallengeWeek>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .Select(row => new PveChallengeWeek(row.WeekStartTime, row.WeekRewardGetNum, row.PrivilegeRewardGetNum))
            .FirstOrDefaultAsync();
    }

    public async Task<long> LoadPrivilegeExpireTimeAsync(ulong gameUlongUID, int activityID)
    {
        await using var database = new DataConnection(options);
        return await ReadPrivilegeExpireTimeAsync(database, gameUlongUID, activityID);
    }

    public async Task<PveChallengeShopCommit> CommitShopBuyAsync(ulong gameUlongUID, PveChallengeShopPurchase purchase)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TaskProcessRepository.LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return PveChallengeShopCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var bought = await database.GetTable<RolePveChallengeShop>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ShopID == purchase.ShopID)
            .Select(row => row.ShopBuyNum)
            .FirstOrDefaultAsync();
        if (bought + purchase.ShopCnt > purchase.BuyNumLimit)
        {
            await transaction.RollbackAsync();
            return PveChallengeShopCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrPveChallengeShopCntInvaild);
        }

        if (purchase.Buff is { } buff && !await IsNextBuffLevelAsync(database, gameUlongUID, buff))
        {
            await transaction.RollbackAsync();
            return PveChallengeShopCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrPveChallengeBuffLevelInvaild);
        }

        var beforeCoinNum = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, purchase.CoinID);
        var debit = new BagChange
        {
            CoinCosts = purchase.CoinCost > 0 ? new Dictionary<int, long> { [purchase.CoinID] = purchase.CoinCost } : new Dictionary<int, long>(),
        };
        var result = await BagRepository.ApplyAsync(database, gameUlongUID, debit, 0);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return PveChallengeShopCommit.Refused(result);
        }

        await database.GetTable<RolePveChallengeShop>().InsertOrUpdateAsync(
            () => new RolePveChallengeShop { GameUlongUID = gameUlongUID, ShopID = purchase.ShopID, ShopBuyNum = purchase.ShopCnt },
            row => new RolePveChallengeShop { ShopBuyNum = row.ShopBuyNum + purchase.ShopCnt });
        if (purchase.Buff is { } newBuff)
        {
            await SaveNewBuffLevelAsync(database, gameUlongUID, newBuff);
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        var state = await ReadShopStateAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new PveChallengeShopCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, beforeCoinNum, wallet, state);
    }

    public async Task<bool> ClearBuffNewTagAsync(ulong gameUlongUID, int buffID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RolePveChallengeBuff>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.BuffID == buffID)
            .Set(row => row.IsNewUnLock, NotNewUnLock)
            .UpdateAsync() == OneRow;
    }

    public async Task<PvePrivilegeCommit> CommitActivatePrivilegeAsync(ulong gameUlongUID, PvePrivilegeActivation activation)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TaskProcessRepository.LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return new PvePrivilegeCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, null);
        }

        if (await ReadPrivilegeExpireTimeAsync(database, gameUlongUID, activation.ActivityID) > activation.Now)
        {
            await transaction.RollbackAsync();
            return new PvePrivilegeCommit(PB_ENUM_RES_ERROR_CODE.PbErrPvePrivilegeAlreadyActivate, null);
        }

        var debit = new BagChange { CoinCosts = new Dictionary<int, long> { [CoinID.Coupons] = activation.CostCoupons } };
        if (activation.CostCoupons > 0
            && await BagRepository.ApplyAsync(database, gameUlongUID, debit, activation.Now) != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return new PvePrivilegeCommit(PB_ENUM_RES_ERROR_CODE.PbErrPvePrivilegeNotEnoughCoupons, null);
        }

        await database.GetTable<RolePvePrivilege>().InsertOrUpdateAsync(
            () => new RolePvePrivilege
            {
                GameUlongUID = gameUlongUID,
                ActivityID = activation.ActivityID,
                PrivilegeExpireTime = activation.PrivilegeExpireTime,
            },
            _ => new RolePvePrivilege { PrivilegeExpireTime = activation.PrivilegeExpireTime });
        if (activation.CouponsFlow is { } couponsFlow)
        {
            await CouponsFlowRepository.AddAsync(database, gameUlongUID, couponsFlow);
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new PvePrivilegeCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet);
    }

    internal static Task SaveWeekAsync(DataConnection database, ulong gameUlongUID, PveChallengeWeek week) =>
        database.GetTable<RolePveChallengeWeek>().InsertOrUpdateAsync(
            () => new RolePveChallengeWeek
            {
                GameUlongUID = gameUlongUID,
                WeekStartTime = week.WeekStartTime,
                WeekRewardGetNum = week.WeekRewardGetNum,
                PrivilegeRewardGetNum = week.PrivilegeRewardGetNum,
            },
            _ => new RolePveChallengeWeek
            {
                WeekStartTime = week.WeekStartTime,
                WeekRewardGetNum = week.WeekRewardGetNum,
                PrivilegeRewardGetNum = week.PrivilegeRewardGetNum,
            });

    internal static async Task<bool> IsNextBuffLevelAsync(DataConnection database, ulong gameUlongUID, PveChallengeBuff buff) =>
        await ReadBuffLevelAsync(database, gameUlongUID, buff.BuffID) == buff.BuffLevel - 1;

    internal static Task SaveNewBuffLevelAsync(DataConnection database, ulong gameUlongUID, PveChallengeBuff buff) =>
        database.GetTable<RolePveChallengeBuff>().InsertOrUpdateAsync(
            () => new RolePveChallengeBuff
            {
                GameUlongUID = gameUlongUID,
                BuffID = buff.BuffID,
                BuffLevel = buff.BuffLevel,
                IsNewUnLock = NewUnLock,
            },
            _ => new RolePveChallengeBuff { BuffLevel = buff.BuffLevel, IsNewUnLock = NewUnLock });

    internal static async Task<PveChallengeShopState> ReadShopStateAsync(DataConnection database, ulong gameUlongUID)
    {
        var buffs = await database.GetTable<RolePveChallengeBuff>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .OrderBy(row => row.BuffID)
            .Select(row => new PveChallengeBuff(row.BuffID, row.BuffLevel, row.IsNewUnLock))
            .ToArrayAsync();
        var shopBuys = await database.GetTable<RolePveChallengeShop>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .OrderBy(row => row.ShopID)
            .Select(row => new PveChallengeShopBuy(row.ShopID, row.ShopBuyNum))
            .ToArrayAsync();
        return new PveChallengeShopState(buffs, shopBuys);
    }

    private static async Task<int> ReadBuffLevelAsync(DataConnection database, ulong gameUlongUID, int buffID) =>
        await database.GetTable<RolePveChallengeBuff>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.BuffID == buffID)
            .Select(row => (int?)row.BuffLevel)
            .FirstOrDefaultAsync() ?? NoBuffLevel;

    private static Task<long> ReadPrivilegeExpireTimeAsync(DataConnection database, ulong gameUlongUID, int activityID) =>
        database.GetTable<RolePvePrivilege>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID)
            .Select(row => row.PrivilegeExpireTime)
            .FirstOrDefaultAsync();
}
