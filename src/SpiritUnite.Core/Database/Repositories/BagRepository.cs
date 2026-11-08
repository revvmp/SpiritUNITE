using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record BagCommit(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<BagItem> ChangedItems, RoleWallet? Wallet)
{
    public IReadOnlyDictionary<int, long> BeforeCoinNums { get; init; } = new Dictionary<int, long>();
    public IReadOnlyList<RoleItemInfo> TimeLimitedPasters { get; init; } = [];

    public static BagCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, [], null);
}

public sealed class BagRepository(DataOptions options)
{
    internal static readonly int[] BagItemTypes = [(int)RoleItemType.OutSideItem, (int)RoleItemType.Badge];

    private const int OneRow = 1;
    private const int NoNewTag = 0;

    public async Task<IReadOnlyList<BagItem>> LoadAsync(ulong gameUlongUID, RoleItemType itemType)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleItem>()
            .Where(item => item.GameUlongUID == gameUlongUID && item.ItemType == (int)itemType)
            .OrderBy(item => item.ItemId)
            .ToArrayAsync();
        return [.. rows.Select(ToBagItem)];
    }

    public async Task<BagItem?> FindAsync(ulong gameUlongUID, RoleItemType itemType, int itemId)
    {
        await using var database = new DataConnection(options);
        var row = await FindRowAsync(database, gameUlongUID, itemType, itemId);
        return row is null ? null : ToBagItem(row);
    }

    public async Task<BagItem?> ClearNewTagAsync(ulong gameUlongUID, RoleItemType itemType, int itemId)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleItem>()
            .Where(item => item.GameUlongUID == gameUlongUID && item.ItemType == (int)itemType && item.ItemId == itemId)
            .Set(item => item.NewTag, NoNewTag)
            .UpdateAsync();
        var row = await FindRowAsync(database, gameUlongUID, itemType, itemId);
        return row is null ? null : ToBagItem(row);
    }

    public async Task<BagCommit> CommitAsync(ulong gameUlongUID, BagChange change, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var coinID in change.CoinGrants.Keys)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var result = await ApplyAsync(database, gameUlongUID, change, now);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(result);
        }

        var changedItems = await ReadItemsAsync(database, gameUlongUID, change.TouchedItems);
        var wallet = change.ChangesCoins ? await WalletRepository.ReadAsync(database, gameUlongUID) : null;
        var timeLimitedPasters = await PasterRepository.ReadTimeLimitedAsync(
            database,
            gameUlongUID,
            change.PasterTimeLimits.Select(grant => grant.PasterId));
        await transaction.CommitAsync();
        return new BagCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, changedItems, wallet)
        {
            BeforeCoinNums = beforeCoinNums,
            TimeLimitedPasters = timeLimitedPasters,
        };
    }

    public async Task<BagCommit> UseRenameCardAsync(ulong gameUlongUID, int renameCardId, string newUserName)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var debited = await database.GetTable<RoleItem>()
            .Where(item => item.GameUlongUID == gameUlongUID
                && item.ItemType == (int)RoleItemType.OutSideItem
                && item.ItemId == renameCardId
                && item.ItemCnt >= OneRow)
            .Set(item => item.ItemCnt, item => item.ItemCnt - OneRow)
            .UpdateAsync();
        if (debited != OneRow)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemNotFoundResInAcnt);
        }

        try
        {
            await database.GetTable<Role>()
                .Where(role => role.GameUlongUID == gameUlongUID)
                .Set(role => role.InGameUsrName, newUserName)
                .UpdateAsync();
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRegisterNameDup);
        }

        var changedItems = await ReadItemsAsync(database, gameUlongUID, [(RoleItemType.OutSideItem, renameCardId)]);
        await transaction.CommitAsync();
        return new BagCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, changedItems, null);
    }

    internal static async Task<PB_ENUM_RES_ERROR_CODE> ApplyAsync(
        DataConnection database,
        ulong gameUlongUID,
        BagChange change,
        long now)
    {
        foreach (var (coinID, cost) in change.CoinCosts)
        {
            var debited = await database.GetTable<RoleCoin>()
                .Where(coin => coin.GameUlongUID == gameUlongUID && coin.CoinID == coinID && coin.CoinVal >= cost)
                .Set(coin => coin.CoinVal, coin => coin.CoinVal - cost)
                .UpdateAsync();
            if (debited != OneRow)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin;
            }
        }

        foreach (var cost in change.Costs)
        {
            var debited = await database.GetTable<RoleItem>()
                .Where(item => item.GameUlongUID == gameUlongUID
                    && item.ItemType == (int)cost.ItemType
                    && item.ItemId == cost.ItemId
                    && item.ItemCnt >= cost.Cnt)
                .Set(item => item.ItemCnt, item => item.ItemCnt - cost.Cnt)
                .UpdateAsync();
            if (debited != OneRow)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemDeleteNotEnough;
            }
        }

        if (change.LevelUp is { } levelUp)
        {
            var raised = await database.GetTable<RoleItem>()
                .Where(item => item.GameUlongUID == gameUlongUID
                    && item.ItemType == (int)RoleItemType.OutSideItem
                    && item.ItemId == levelUp.ItemId
                    && item.ItemCnt > 0
                    && item.ItemLevel == levelUp.FromLevel)
                .Set(item => item.ItemLevel, levelUp.FromLevel + 1)
                .UpdateAsync();
            if (raised != OneRow)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemNotFoundResInAcnt;
            }
        }

        var granted = await ApplyGrantsAsync(database, gameUlongUID, change.Grants, now);
        if (granted != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return granted;
        }

        foreach (var (coinID, grant) in change.CoinGrants)
        {
            await WalletRepository.CreditAsync(database, gameUlongUID, coinID, grant);
        }

        try
        {
            foreach (var pokemon in change.PokemonGrants)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, pokemon));
            }

            foreach (var item in change.ItemGrants)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed;
        }

        await PasterRepository.ApplyTimeLimitedAsync(database, gameUlongUID, change.PasterTimeLimits, now);
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    internal static async Task<PB_ENUM_RES_ERROR_CODE> ApplyGrantsAsync(
        DataConnection database,
        ulong gameUlongUID,
        IEnumerable<BagGrant> grants,
        long now)
    {
        foreach (var grant in grants)
        {
            if (grant.Cnt <= 0 || grant.Cnt > grant.StackSize)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemBagFull;
            }

            var stack = database.GetTable<RoleItem>()
                .Where(item => item.GameUlongUID == gameUlongUID
                    && item.ItemType == (int)grant.ItemType
                    && item.ItemId == grant.ItemId
                    && item.ItemCnt <= grant.StackSize - grant.Cnt)
                .Set(item => item.ItemCnt, item => item.ItemCnt + grant.Cnt)
                .Set(item => item.GainTime, now);
            if (grant.NewTag != NoNewTag)
            {
                stack = stack.Set(item => item.NewTag, grant.NewTag);
            }

            var stacked = await stack.UpdateAsync();
            if (stacked == OneRow)
            {
                continue;
            }

            if (await FindRowAsync(database, gameUlongUID, grant.ItemType, grant.ItemId) is not null)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemBagFull;
            }

            try
            {
                await database.InsertAsync(new RoleItem
                {
                    GameUlongUID = gameUlongUID,
                    ItemType = (int)grant.ItemType,
                    ItemId = grant.ItemId,
                    GainTime = now,
                    TimeLimited = RoleItemInfo.Permanent,
                    NewTag = grant.NewTag,
                    ItemCnt = grant.Cnt,
                    ItemLevel = grant.InitialLevel,
                });
            }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                return PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemAddFail;
            }
        }

        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    internal static async Task<IReadOnlyList<BagItem>> ReadItemsAsync(
        DataConnection database,
        ulong gameUlongUID,
        IEnumerable<(RoleItemType ItemType, int ItemId)> keys)
    {
        var items = new List<BagItem>();
        foreach (var (itemType, itemId) in keys)
        {
            if (await FindRowAsync(database, gameUlongUID, itemType, itemId) is { } row)
            {
                items.Add(ToBagItem(row));
            }
        }

        return items;
    }

    private static Task<RoleItem?> FindRowAsync(DataConnection database, ulong gameUlongUID, RoleItemType itemType, int itemId) =>
        database.GetTable<RoleItem>().FirstOrDefaultAsync(item =>
            item.GameUlongUID == gameUlongUID && item.ItemType == (int)itemType && item.ItemId == itemId);

    private static BagItem ToBagItem(RoleItem row) => new(
        (RoleItemType)row.ItemType,
        row.ItemId,
        row.ItemCnt,
        row.ItemLevel,
        row.GainTime,
        row.NewTag);
}
