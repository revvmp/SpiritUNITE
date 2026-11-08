using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.BattlePass;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record BattlePassStored(BattlePassProgress Progress, IReadOnlySet<BattlePassLevelRewardKey> ClaimedRewards);

public sealed record BattlePassTaskRewardRecord(int TaskId, long PeriodStart, long RewardTime);

public sealed record BattlePassMutation(
    BattlePassProgress Progress,
    IReadOnlyList<BattlePassLevelRewardKey> ClaimedRewards,
    BattlePassGrant Grant,
    IReadOnlyList<BattlePassTaskRewardRecord> TaskRewards,
    CouponsFlowEntry? CouponsFlow = null);

public sealed record BattlePassDecision(PB_ENUM_RES_ERROR_CODE Result, BattlePassMutation? Mutation)
{
    public static BattlePassDecision Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null);
}

public sealed record BattlePassCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    BattlePassStored? Stored,
    BattlePassMutation? Mutation,
    RoleWallet? Wallet,
    IReadOnlyList<BagItem> ChangedItems,
    IReadOnlyDictionary<int, long> SpecialCoinsBefore)
{
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;

    public static BattlePassCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, null, null, [], new Dictionary<int, long>());
}

public sealed class BattlePassRepository(DataOptions options)
{
    private const int OneRow = 1;
    private const int Settled = 1;
    private const int NotSettled = 0;

    public async Task<BattlePassStored> LoadAsync(ulong gameUlongUID, int seasonID)
    {
        await using var database = new DataConnection(options);
        var row = await FindRowAsync(database, gameUlongUID, seasonID) ?? await CreateRowAsync(database, gameUlongUID, seasonID);
        return new BattlePassStored(ToProgress(row), await ReadClaimedAsync(database, gameUlongUID, seasonID));
    }

    public async Task<BattlePassProgress[]> ListAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return [.. (await database.GetTable<RoleBattlePass>()
                .Where(row => row.GameUlongUID == gameUlongUID)
                .OrderBy(row => row.SeasonID)
                .ToArrayAsync())
            .Select(ToProgress)];
    }

    public async Task<IReadOnlySet<BattlePassLevelRewardKey>> FindClaimedAsync(ulong gameUlongUID, int seasonID)
    {
        await using var database = new DataConnection(options);
        return await ReadClaimedAsync(database, gameUlongUID, seasonID);
    }

    public async Task<BattlePassTaskRewardRecord[]> FindTaskRewardsAsync(ulong gameUlongUID, long periodStartSince)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleBattlePassTaskReward>()
            .Where(reward => reward.GameUlongUID == gameUlongUID && reward.PeriodStart >= periodStartSince)
            .Select(reward => new BattlePassTaskRewardRecord(reward.TaskId, reward.PeriodStart, reward.RewardTime))
            .ToArrayAsync();
    }

    public async Task<bool> MarkSettledAsync(ulong gameUlongUID, int seasonID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleBattlePass>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.SeasonID == seasonID && row.SeasonSettled == NotSettled)
            .Set(row => row.SeasonSettled, Settled)
            .UpdateAsync() == OneRow;
    }

    public async Task<BattlePassCommit> CommitAsync(
        ulong gameUlongUID,
        int seasonID,
        Func<BattlePassStored, BattlePassDecision> decide,
        long now)
    {
        await using var database = new DataConnection(options);
        if (await FindRowAsync(database, gameUlongUID, seasonID) is null)
        {
            await CreateRowAsync(database, gameUlongUID, seasonID);
        }

        await using var transaction = await database.BeginTransactionAsync();
        var locked = await database.GetTable<RoleBattlePass>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.SeasonID == seasonID)
            .AsMySql()
            .ForUpdateHint()
            .ToArrayAsync();
        if (locked is not [var row])
        {
            await transaction.RollbackAsync();
            return BattlePassCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var stored = new BattlePassStored(ToProgress(row), await ReadClaimedAsync(database, gameUlongUID, seasonID));
        var decision = decide(stored);
        if (decision.Mutation is not { } mutation || decision.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return BattlePassCommit.Refused(decision.Result);
        }

        try
        {
            foreach (var claimed in mutation.ClaimedRewards)
            {
                await database.InsertAsync(new RoleBattlePassLevelReward
                {
                    GameUlongUID = gameUlongUID,
                    SeasonID = seasonID,
                    Level = claimed.Level,
                    RewardType = (int)claimed.Type,
                    RewardTime = now,
                });
            }

            foreach (var taskReward in mutation.TaskRewards)
            {
                await database.InsertAsync(new RoleBattlePassTaskReward
                {
                    GameUlongUID = gameUlongUID,
                    TaskId = taskReward.TaskId,
                    PeriodStart = taskReward.PeriodStart,
                    RewardTime = taskReward.RewardTime,
                });
            }

            foreach (var item in mutation.Grant.Items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }

            foreach (var pokemonId in mutation.Grant.PokemonIds)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, new RolePokemonInfo(
                    pokemonId,
                    now,
                    (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonBattlepassLevelReward,
                    RolePokemonInfo.HasNewTag)));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return BattlePassCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        }

        var change = mutation.Grant.Change;
        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var coinID in change.CoinGrants.Keys.Where(CoinID.IsSpecialCoin))
        {
            specialCoinsBefore[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, change, now);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return BattlePassCommit.Refused(applied);
        }

        if (mutation.CouponsFlow is { } couponsFlow)
        {
            await CouponsFlowRepository.AddAsync(database, gameUlongUID, couponsFlow);
        }

        await database.UpdateAsync(ToRow(gameUlongUID, mutation.Progress));
        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        var claimedAfter = await ReadClaimedAsync(database, gameUlongUID, seasonID);
        await transaction.CommitAsync();
        return new BattlePassCommit(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            new BattlePassStored(mutation.Progress, claimedAfter),
            mutation,
            wallet,
            changedItems,
            specialCoinsBefore);
    }

    private static Task<RoleBattlePass?> FindRowAsync(DataConnection database, ulong gameUlongUID, int seasonID) =>
        database.GetTable<RoleBattlePass>().FirstOrDefaultAsync(row => row.GameUlongUID == gameUlongUID && row.SeasonID == seasonID);

    private static async Task<RoleBattlePass> CreateRowAsync(DataConnection database, ulong gameUlongUID, int seasonID)
    {
        var row = ToRow(gameUlongUID, BattlePassProgress.Starting(seasonID));
        try
        {
            await database.InsertAsync(row);
            return row;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return (await FindRowAsync(database, gameUlongUID, seasonID))!;
        }
    }

    private static async Task<IReadOnlySet<BattlePassLevelRewardKey>> ReadClaimedAsync(DataConnection database, ulong gameUlongUID, int seasonID) =>
        (await database.GetTable<RoleBattlePassLevelReward>()
            .Where(reward => reward.GameUlongUID == gameUlongUID && reward.SeasonID == seasonID)
            .ToArrayAsync())
        .Select(reward => new BattlePassLevelRewardKey(reward.Level, (PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE)reward.RewardType))
        .ToHashSet();

    private static BattlePassProgress ToProgress(RoleBattlePass row) => new(
        row.SeasonID,
        row.Level,
        row.Exp,
        row.TotalExp,
        row.PassType,
        row.VideoPlayed,
        row.AdditionTreasureCnt,
        row.AdditionTreasureUnGetCnt,
        row.AdditionTreasureUnGetExp,
        row.LastUpLevelTime,
        row.LastGainExpTime,
        row.SeasonSettled == Settled);

    private static RoleBattlePass ToRow(ulong gameUlongUID, BattlePassProgress progress) => new()
    {
        GameUlongUID = gameUlongUID,
        SeasonID = progress.SeasonID,
        Level = progress.Level,
        Exp = progress.Exp,
        TotalExp = progress.TotalExp,
        PassType = progress.PassType,
        VideoPlayed = progress.VideoPlayed,
        AdditionTreasureCnt = progress.AdditionTreasureCnt,
        AdditionTreasureUnGetCnt = progress.AdditionTreasureUnGetCnt,
        AdditionTreasureUnGetExp = progress.AdditionTreasureUnGetExp,
        LastUpLevelTime = progress.LastUpLevelTime,
        LastGainExpTime = progress.LastGainExpTime,
        SeasonSettled = progress.SeasonSettled ? Settled : NotSettled,
    };
}
