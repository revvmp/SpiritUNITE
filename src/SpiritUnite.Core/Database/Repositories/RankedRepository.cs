using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record RankedEntry(RankedPosition Before, int TicketConsume);

public sealed record RankedSeasonRecord(
    int SeasonId,
    RankedPosition Position,
    int HighestRankedDivision,
    int HighestMasterPoint,
    int BattleCnt,
    int WinCnt,
    RankedEntry? Entry,
    bool UseFreeTicket = false);

public sealed record RankedSeasonRanking(ulong GameUlongUID, RankedPosition Position);

public sealed record RankedSettlementCommit(bool Committed, IReadOnlyList<BagItem> ChangedItems);

public sealed record RankedRoleRecord(int ProtectCardSelectStatus, long BanRankRaceEndTime, long NoRemindedBuyProtectCardDay);

public sealed record RankedGrantCommit(bool Committed, RoleWallet? Wallet, IReadOnlyList<BagItem> ChangedItems)
{
    public static RankedGrantCommit Refused { get; } = new(false, null, []);
}

public sealed class RankedRepository(DataOptions options)
{
    private const int NoEntryRankedDivision = 0;

    public async Task<RankedRoleRecord?> FindRoleAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleRanked>().FirstOrDefaultAsync(ranked => ranked.GameUlongUID == gameUlongUID);
        return row is null ? null : new RankedRoleRecord(row.ProtectCardSelectStatus, row.BanRankRaceEndTime, row.NoRemindedBuyProtectCardDay);
    }

    public async Task SaveProtectCardSelectStatusAsync(ulong gameUlongUID, int protectCardSelectStatus)
    {
        await using var database = new DataConnection(options);
        var updated = await database.GetTable<RoleRanked>()
            .Where(ranked => ranked.GameUlongUID == gameUlongUID)
            .Set(ranked => ranked.ProtectCardSelectStatus, protectCardSelectStatus)
            .UpdateAsync();
        if (updated > 0)
        {
            return;
        }

        try
        {
            await database.InsertAsync(new RoleRanked
            {
                GameUlongUID = gameUlongUID,
                ProtectCardSelectStatus = protectCardSelectStatus,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await database.GetTable<RoleRanked>()
                .Where(ranked => ranked.GameUlongUID == gameUlongUID)
                .Set(ranked => ranked.ProtectCardSelectStatus, protectCardSelectStatus)
                .UpdateAsync();
        }
    }

    public async Task SaveNoRemindedBuyProtectCardDayAsync(ulong gameUlongUID, long noRemindedBuyProtectCardDay)
    {
        await using var database = new DataConnection(options);
        var updated = await database.GetTable<RoleRanked>()
            .Where(ranked => ranked.GameUlongUID == gameUlongUID)
            .Set(ranked => ranked.NoRemindedBuyProtectCardDay, noRemindedBuyProtectCardDay)
            .UpdateAsync();
        if (updated > 0)
        {
            return;
        }

        try
        {
            await database.InsertAsync(new RoleRanked
            {
                GameUlongUID = gameUlongUID,
                NoRemindedBuyProtectCardDay = noRemindedBuyProtectCardDay,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await database.GetTable<RoleRanked>()
                .Where(ranked => ranked.GameUlongUID == gameUlongUID)
                .Set(ranked => ranked.NoRemindedBuyProtectCardDay, noRemindedBuyProtectCardDay)
                .UpdateAsync();
        }
    }

    public async Task<RankedSeasonRecord?> FindSeasonAsync(ulong gameUlongUID, int seasonId)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleRankedSeason>()
            .FirstOrDefaultAsync(season => season.GameUlongUID == gameUlongUID && season.SeasonId == seasonId);
        return row is null ? null : ToRecord(row);
    }

    public async Task<RankedSeasonRecord?> FindLatestSeasonAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleRankedSeason>()
            .Where(season => season.GameUlongUID == gameUlongUID)
            .OrderByDescending(season => season.SeasonId)
            .FirstOrDefaultAsync();
        return row is null ? null : ToRecord(row);
    }

    public async Task<IReadOnlyList<RankedSeasonRanking>> ListPlacedSeasonAsync(int seasonId)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleRankedSeason>()
            .Where(season => season.SeasonId == seasonId && season.BattleCnt > 0)
            .Select(season => new { season.GameUlongUID, season.RankedDivision, season.WinPoint, season.MasterPoint })
            .ToListAsync();
        return [.. rows.Select(row => new RankedSeasonRanking(row.GameUlongUID, new RankedPosition(row.RankedDivision, row.WinPoint, row.MasterPoint)))];
    }

    public async Task<int[]> FindTaskAwardsAsync(ulong gameUlongUID, int seasonId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleRankedSeasonTaskAward>()
            .Where(award => award.GameUlongUID == gameUlongUID && award.SeasonId == seasonId)
            .Select(award => award.RankedDivision)
            .ToArrayAsync();
    }

    public async Task<RankedGrantCommit> CommitSeasonRolloverAsync(ulong gameUlongUID, RankedSeasonRecord newSeason, RankedGrant grant, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(ToRow(gameUlongUID, newSeason));
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return RankedGrantCommit.Refused;
        }

        return await ApplyGrantAndCommitAsync(database, transaction, gameUlongUID, grant, now);
    }

    public async Task<RankedGrantCommit> CommitTaskAwardAsync(
        ulong gameUlongUID,
        int seasonId,
        IReadOnlyCollection<int> rankedDivisions,
        RankedGrant grant,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            foreach (var rankedDivision in rankedDivisions)
            {
                await database.InsertAsync(new RoleRankedSeasonTaskAward
                {
                    GameUlongUID = gameUlongUID,
                    SeasonId = seasonId,
                    RankedDivision = rankedDivision,
                });
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return RankedGrantCommit.Refused;
        }

        return await ApplyGrantAndCommitAsync(database, transaction, gameUlongUID, grant, now);
    }

    public async Task<IReadOnlyList<RankedSeasonRecord>> ListSeasonsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleRankedSeason>()
            .Where(season => season.GameUlongUID == gameUlongUID)
            .OrderByDescending(season => season.SeasonId)
            .ToArrayAsync();
        return [.. rows.Select(ToRecord)];
    }

    public async Task SaveSeasonAsync(ulong gameUlongUID, RankedSeasonRecord record)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(ToRow(gameUlongUID, record));
    }

    public async Task<RankedSettlementCommit> CommitSeasonAsync(
        ulong gameUlongUID,
        RankedSeasonRecord record,
        BagCost? itemCost,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        IReadOnlyList<BagItem> changedItems = [];
        if (itemCost is { } cost)
        {
            var change = new BagChange { Costs = [cost] };
            if (await BagRepository.ApplyAsync(database, gameUlongUID, change, now) != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                await transaction.RollbackAsync();
                return new RankedSettlementCommit(false, []);
            }

            changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, change.TouchedItems);
        }

        await database.InsertOrReplaceAsync(ToRow(gameUlongUID, record));
        await transaction.CommitAsync();
        return new RankedSettlementCommit(true, changedItems);
    }

    private static async Task<RankedGrantCommit> ApplyGrantAndCommitAsync(
        DataConnection database,
        DataConnectionTransaction transaction,
        ulong gameUlongUID,
        RankedGrant grant,
        long now)
    {
        try
        {
            foreach (var item in grant.Items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return RankedGrantCommit.Refused;
        }

        if (await BagRepository.ApplyAsync(database, gameUlongUID, grant.Change, now) != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return RankedGrantCommit.Refused;
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, grant.Change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new RankedGrantCommit(true, wallet, changedItems);
    }

    private static RankedSeasonRecord ToRecord(RoleRankedSeason row) => new(
        row.SeasonId,
        new RankedPosition(row.RankedDivision, row.WinPoint, row.MasterPoint),
        row.HighestRankedDivision,
        row.HighestMasterPoint,
        row.BattleCnt,
        row.WinCnt,
        row.EntryRankedDivision == NoEntryRankedDivision
            ? null
            : new RankedEntry(new RankedPosition(row.EntryRankedDivision, row.EntryWinPoint, row.EntryMasterPoint), row.EntryTicketConsume),
        row.UseFreeTicket);

    private static RoleRankedSeason ToRow(ulong gameUlongUID, RankedSeasonRecord record) => new()
    {
        GameUlongUID = gameUlongUID,
        SeasonId = record.SeasonId,
        RankedDivision = record.Position.RankedDivision,
        WinPoint = record.Position.WinPoint,
        MasterPoint = record.Position.MasterPoint,
        HighestRankedDivision = record.HighestRankedDivision,
        HighestMasterPoint = record.HighestMasterPoint,
        BattleCnt = record.BattleCnt,
        WinCnt = record.WinCnt,
        EntryRankedDivision = record.Entry?.Before.RankedDivision ?? NoEntryRankedDivision,
        EntryWinPoint = record.Entry?.Before.WinPoint ?? 0,
        EntryMasterPoint = record.Entry?.Before.MasterPoint ?? 0,
        EntryTicketConsume = record.Entry?.TicketConsume ?? 0,
        UseFreeTicket = record.UseFreeTicket,
    };
}
