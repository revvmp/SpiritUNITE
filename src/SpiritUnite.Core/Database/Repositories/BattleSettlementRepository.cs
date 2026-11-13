using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Energy;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Packets;

namespace SpiritUnite.Database.Repositories;

public sealed record PveChallengeNodeKey(int StageID, int NodeID, int DifficultyLevel);

public sealed record BattleSettlementChange(
    int ChgExp,
    int PokemonId,
    int PokemonPoint,
    long BattleWinTime,
    RolePveChallengeStar? PveChallengeStar,
    ChallengeTrainerMapLevel? HeavenTrainerChallenge,
    IReadOnlyList<BagGrant> Grants,
    IReadOnlyDictionary<int, long> Coins,
    RoleFightHistory FightHistory,
    int MaxFightHistoryNumPerMapSubMode,
    BattleEnergyGrant? Energy,
    UsedPokemonBattle? CountedBattle = null,
    WarmBattleGame? CountedGame = null)
{
    public PveChallengeWeek? PveChallengeWeek { get; init; }

    public BattleIntimacyGain? Intimacy { get; init; }

    public PveSettlementEligibility? PveBonus { get; init; }

    public IReadOnlyList<BagGrant> HeldItemUnlockGrants { get; init; } = [];
}

public sealed record BattleIntimacyGain(IReadOnlyList<ulong> FriendUids, int IntimacyValue);

public sealed record BattleEnergyGrant(int EnergyGain, EnergyRules Rules);

public sealed record ChallengeTrainerMapLevel(int MapId, int LevelType);

public sealed record BattleEnergyChange(RoleEnergyInfo Before, RoleEnergyInfo After)
{
    public int NormalGain => Before.CollectableCapacity - After.CollectableCapacity;

    public int AcceleratedGain => Before.AcceleratorCapacityTotal - After.AcceleratorCapacityTotal;

    public int EnergyGain => NormalGain + AcceleratedGain;
}

public sealed record BattleSettlementCommit(
    IReadOnlyList<BagItem> ChangedItems,
    IReadOnlyList<BagGrant> ClampedGrants,
    IReadOnlyDictionary<int, long> BeforeCoinNums,
    RoleWallet Wallet,
    UsedPokemonInfo? UsedPokemon,
    ChallengeTrainerRecord? HeavenTrainerRecord,
    BattleEnergyChange? Energy)
{
    public IReadOnlyList<BagGrant> HeldItemUnlockGrants { get; init; } = [];

    public byte[] Checkpoint { get; init; } = [];

    public int RoleTotalExp { get; init; }
}

public sealed record SettlementResponse(pbSCMultiGameSettleGainRsp Gain, GamePacket[] Packets, GamePacket[] EventPackets);

public sealed class BattleSettlementRepository(DataOptions options)
{
    private const int OneRow = 1;
    private const int NoPokemon = 0;
    private const int NoStar = 0;
    private const int OneChallenge = 1;
    private const int OneUse = 1;

    public async Task<BattleSettlementCommit?> CommitAsync(
        ulong gameUlongUID,
        SettlementKey? journalEntry,
        PveChallengeNodeKey? node,
        Func<int, BattleSettlementChange> changeAfterStoredStarNum,
        long now,
        Func<BattleSettlementCommit, byte[]>? checkpoint = null)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (journalEntry is not null)
        {
            await SettlementEffects.LockRoleAsync(database, gameUlongUID);
            if (!await SettlementJournal.IsProcessingAsync(database, journalEntry))
            {
                throw new SettlementClaimLostException(journalEntry);
            }
        }

        var result = journalEntry is null
            ? await CommitCoreAsync(database, gameUlongUID, node, changeAfterStoredStarNum, now, checkpoint)
            : (await SettlementEffects.RunAsync(database, journalEntry, "core", () =>
                CommitCoreAsync(database, gameUlongUID, node, changeAfterStoredStarNum, now, checkpoint))).Value;
        if (result is null)
        {
            await transaction.RollbackAsync();
            return null;
        }

        await transaction.CommitAsync();
        return result;
    }

    public async Task<BattleSettlementCommit?> FindCommittedAsync(SettlementKey key)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<SettlementEffectRecord>()
            .Where(row => row.SettlementID == key.SettlementID && row.GameUlongUID == key.GameUlongUID && row.Effect == "core")
            .Select(row => row.Output).ToArrayAsync();
        return rows.Length == 0 ? null : SettlementEffects.Deserialize<BattleSettlementCommit>(rows[0]);
    }

    public async Task<SettlementResponse> SaveResponseAndCompleteAsync(SettlementKey key, SettlementResponse response, long completedAt)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await SettlementEffects.LockRoleAsync(database, key.GameUlongUID);
        await SettlementOwnership.VerifyAsync(database, key.GameUlongUID);
        if (!await SettlementJournal.IsProcessingAsync(database, key))
        {
            throw new SettlementClaimLostException(key);
        }

        var saved = await SettlementEffects.RunAsync(database, key, "response", () => Task.FromResult(response), lockRole: false);
        await SettlementJournal.CompleteAsync(database, key, completedAt);
        await transaction.CommitAsync();
        return saved.Value;
    }

    public async Task<SettlementResponse?> FindResponseAsync(SettlementKey key)
    {
        await using var database = new DataConnection(options);
        var output = await database.GetTable<SettlementEffectRecord>()
            .Where(row => row.SettlementID == key.SettlementID && row.GameUlongUID == key.GameUlongUID && row.Effect == "response")
            .Select(row => row.Output).FirstOrDefaultAsync();
        return output is null ? null : SettlementEffects.Deserialize<SettlementResponse>(output);
    }

    public async Task<(SettlementKey Key, pbSCMultiGameSettleGainRsp Gain)?> FindLatestGainAsync(ulong uid, long minimumUpdatedAt)
    {
        await using var database = new DataConnection(options);
        var latest = await database.GetTable<BattleSettlementRecord>().Where(row => row.GameUlongUID == uid)
            .OrderByDescending(row => row.BattleStartedAt).ThenByDescending(row => row.SettlementID).FirstOrDefaultAsync();
        if (latest is null || latest.UpdatedAt < minimumUpdatedAt)
        {
            return null;
        }

        var output = await database.GetTable<SettlementEffectRecord>()
            .Where(row => row.SettlementID == latest.SettlementID && row.GameUlongUID == uid && row.Effect == "response")
            .Select(row => row.Output).FirstOrDefaultAsync();
        return output is null ? null : (SettlementKey.Of(latest), SettlementEffects.Deserialize<SettlementResponse>(output).Gain);
    }

    private static async Task<BattleSettlementCommit?> CommitCoreAsync(
        DataConnection database,
        ulong gameUlongUID,
        PveChallengeNodeKey? node,
        Func<int, BattleSettlementChange> changeAfterStoredStarNum,
        long now,
        Func<BattleSettlementCommit, byte[]>? checkpoint)
    {
        if (!await LockRoleAsync(database, gameUlongUID))
        {
            return null;
        }

        var storedStarNum = node is null ? NoStar : await ReadStarNumAsync(database, gameUlongUID, node);
        var change = changeAfterStoredStarNum(storedStarNum);
        if (change.CountedBattle is { } countedBattle)
        {
            await ProfileStatisticRepository.RecordAsync(database, gameUlongUID, countedBattle, change.FightHistory);
        }
        if (await AddRoleTotalExpAsync(database, gameUlongUID, change.ChgExp) != OneRow)
        {
            return null;
        }

        var usedPokemon = change.PokemonId == NoPokemon
            ? null
            : await AddPokemonPointAsync(database, gameUlongUID, change, now);
        var heldItemUnlockGrants = await FilterHeldItemUnlockGrantsAsync(database, gameUlongUID, change.HeldItemUnlockGrants);
        BagGrant[] grants = [.. heldItemUnlockGrants, .. change.Grants];
        var clampedGrants = await ApplyGrantsUpToStackAsync(database, gameUlongUID, grants, now);
        if (clampedGrants is null)
        {
            return null;
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var (coinID, amount) in change.Coins)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
            await WalletRepository.CreditAsync(database, gameUlongUID, coinID, amount);
        }

        var energy = change.Energy is { } energyGrant
            ? await GainEnergyAsync(database, gameUlongUID, energyGrant, DateTimeOffset.FromUnixTimeSeconds(now))
            : null;
        if (change.CountedGame is { } countedGame)
        {
            await WarmBattleRepository.AddGameAsync(database, gameUlongUID, countedGame);
        }

        if (change.PveChallengeWeek is { } week)
        {
            await PveChallengeRepository.SaveWeekAsync(database, gameUlongUID, week);
        }

        if (change.PveBonus is { } bonus)
        {
            await PveSettlementRepository.SaveAsync(database, gameUlongUID, bonus);
        }

        if (change.PveChallengeStar is { } star && star.StarNum > storedStarNum)
        {
            await RecordPveChallengeStarAsync(database, gameUlongUID, star, now);
        }

        var heavenTrainerRecord = change.HeavenTrainerChallenge is { } challenge
            ? await AddChallengeCntAsync(database, gameUlongUID, challenge)
            : null;
        await FightHistoryRepository.AddAsync(database, change.FightHistory, change.MaxFightHistoryNumPerMapSubMode);
        if (change.Intimacy is { } intimacy)
        {
            await FriendRepository.AddTogetherBattleAsync(database, gameUlongUID, intimacy.FriendUids, intimacy.IntimacyValue);
        }

        var changedItems = await BagRepository.ReadItemsAsync(
            database,
            gameUlongUID,
            grants.Select(grant => (grant.ItemType, grant.ItemId)).Distinct());
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        var result = new BattleSettlementCommit(changedItems, clampedGrants, beforeCoinNums, wallet, usedPokemon, heavenTrainerRecord, energy)
        {
            HeldItemUnlockGrants = heldItemUnlockGrants,
            RoleTotalExp = await database.GetTable<Role>().Where(row => row.GameUlongUID == gameUlongUID).Select(row => row.RoleTotalExp).SingleAsync(),
        };
        return checkpoint is null ? result : result with { Checkpoint = checkpoint(result) };
    }

    private static async Task<IReadOnlyList<BagGrant>> FilterHeldItemUnlockGrantsAsync(
        DataConnection database,
        ulong gameUlongUID,
        IReadOnlyList<BagGrant> grants)
    {
        if (grants.Count == 0)
        {
            return [];
        }

        var owned = (await BagRepository.ReadOwnedItemIdsAsync(database, gameUlongUID, RoleItemType.OutSideItem)).ToHashSet();
        return [.. grants.Where(grant => !owned.Contains(grant.ItemId))];
    }

    private static async Task<BattleEnergyChange> GainEnergyAsync(DataConnection database, ulong gameUlongUID, BattleEnergyGrant grant, DateTimeOffset now)
    {
        var stored = await EnergyRepository.ReadOrCreateForUpdateAsync(database, gameUlongUID, EnergyCenter.Starting(now, grant.Rules));
        var before = stored.FlushedAt(now, grant.Rules.WeeklyEnergyFreeCapacity);
        var after = before.GainFromBattle(grant.EnergyGain, grant.Rules.EnergyTicketCostEnergy);
        await EnergyRepository.SaveAsync(database, gameUlongUID, before, after);
        return new BattleEnergyChange(before, after);
    }

    private static async Task<bool> LockRoleAsync(DataConnection database, ulong gameUlongUID) =>
        (await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .Select(role => role.GameUlongUID)
            .ToArrayAsync()).Length == OneRow;

    private static async Task<int> ReadStarNumAsync(DataConnection database, ulong gameUlongUID, PveChallengeNodeKey node)
    {
        var starNums = await database.GetTable<RolePveChallengeNode>()
            .Where(row => row.GameUlongUID == gameUlongUID
                && row.StageID == node.StageID
                && row.NodeID == node.NodeID
                && row.DifficultyLevel == node.DifficultyLevel)
            .Select(row => row.StarNum)
            .ToArrayAsync();
        return starNums.DefaultIfEmpty(NoStar).Max();
    }

    private static async Task<IReadOnlyList<BagGrant>?> ApplyGrantsUpToStackAsync(
        DataConnection database,
        ulong gameUlongUID,
        IEnumerable<BagGrant> grants,
        long now)
    {
        var clamped = new List<BagGrant>();
        foreach (var grant in grants.Where(grant => grant.Cnt > 0))
        {
            var applicable = grant with { Cnt = Math.Max(0, Math.Min(grant.Cnt, grant.StackSize)) };
            var result = applicable.Cnt > 0
                ? await BagRepository.ApplyGrantsAsync(database, gameUlongUID, [applicable], now)
                : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
            if (result == PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemBagFull)
            {
                var held = (await BagRepository.ReadItemsAsync(database, gameUlongUID, [(grant.ItemType, grant.ItemId)]))
                    .Sum(item => item.Cnt);
                applicable = grant with { Cnt = Math.Clamp(grant.StackSize - held, 0, applicable.Cnt) };
                result = applicable.Cnt > 0
                    ? await BagRepository.ApplyGrantsAsync(database, gameUlongUID, [applicable], now)
                    : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
            }

            if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                return null;
            }

            if (applicable.Cnt != grant.Cnt)
            {
                clamped.Add(applicable);
            }
        }

        return clamped;
    }

    private static Task<int> AddRoleTotalExpAsync(DataConnection database, ulong gameUlongUID, int chgExp) =>
        database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .Set(role => role.RoleHistoryMaxTotalExp, role => role.RoleHistoryMaxTotalExp > role.RoleTotalExp + chgExp
                ? role.RoleHistoryMaxTotalExp
                : role.RoleTotalExp + chgExp)
            .Set(role => role.RoleTotalExp, role => role.RoleTotalExp + chgExp)
            .UpdateAsync();

    private static async Task<UsedPokemonInfo> AddPokemonPointAsync(DataConnection database, ulong gameUlongUID, BattleSettlementChange change, long now)
    {
        var emptyHeldItemDesign = UsedPokemonInfo.Unused(change.PokemonId).BuildHeldItemDesignInfo().ToByteArray();
        var point = change.PokemonPoint;
        var winTime = change.BattleWinTime;
        await database.GetTable<RolePokemonUsed>().InsertOrUpdateAsync(
            () => new RolePokemonUsed
            {
                GameUlongUID = gameUlongUID,
                PokemonId = change.PokemonId,
                HeldItemDesignInfo = emptyHeldItemDesign,
                PokemonTotalPoint = point,
                PokemonSeasonPoint = point,
                LastBattleWinTime = winTime,
                UseCount = OneUse,
                LastUseTime = now,
            },
            saved => new RolePokemonUsed
            {
                PokemonTotalPoint = saved.PokemonTotalPoint + point,
                PokemonSeasonPoint = saved.PokemonSeasonPoint + point,
                LastBattleWinTime = winTime == UsedPokemonInfo.NeverWon ? saved.LastBattleWinTime : winTime,
                UseCount = saved.UseCount < int.MaxValue ? saved.UseCount + OneUse : saved.UseCount,
                LastUseTime = now,
            });
        var row = await database.GetTable<RolePokemonUsed>()
            .FirstAsync(used => used.GameUlongUID == gameUlongUID && used.PokemonId == change.PokemonId);
        if (change.CountedBattle is not { } battle)
        {
            return PokemonUsedRepository.ToUsedPokemonInfo(row);
        }

        var statistics = PokemonUsedRepository.ToUsedPokemonInfo(row).BattleStatistics.Record(battle);
        await database.GetTable<RolePokemonUsed>()
            .Where(used => used.GameUlongUID == gameUlongUID && used.PokemonId == change.PokemonId)
            .Set(used => used.StatisticInfo, statistics.Total.ToByteArray())
            .Set(used => used.CurrentSeasonID, statistics.CurrentSeasonID)
            .Set(used => used.CurrentSeasonStatisticInfo, statistics.CurrentSeason.ToByteArray())
            .Set(used => used.CurrentWeekStart, statistics.CurrentWeekStart)
            .Set(used => used.CurrentWeekStatisticInfo, statistics.CurrentWeek.ToByteArray())
            .Set(used => used.LastBattleTime, statistics.LastBattleTime)
            .Set(used => used.RankedMatchInfoInSeason, statistics.RankedMatchInfoInSeason.ToByteArray())
            .UpdateAsync();
        return PokemonUsedRepository.ToUsedPokemonInfo(row) with { Statistics = statistics };
    }

    private static async Task<ChallengeTrainerRecord> AddChallengeCntAsync(
        DataConnection database,
        ulong gameUlongUID,
        ChallengeTrainerMapLevel challenge)
    {
        await database.GetTable<RoleChallengeTrainerRecord>().InsertOrUpdateAsync(
            () => new RoleChallengeTrainerRecord
            {
                GameUlongUID = gameUlongUID,
                MapId = challenge.MapId,
                LevelType = challenge.LevelType,
                ChallengeCnt = OneChallenge,
            },
            saved => new RoleChallengeTrainerRecord
            {
                ChallengeCnt = saved.ChallengeCnt < int.MaxValue ? saved.ChallengeCnt + OneChallenge : saved.ChallengeCnt,
            });
        var challengeCnt = await database.GetTable<RoleChallengeTrainerRecord>()
            .Where(record => record.GameUlongUID == gameUlongUID && record.MapId == challenge.MapId && record.LevelType == challenge.LevelType)
            .Select(record => record.ChallengeCnt)
            .FirstAsync();
        return new ChallengeTrainerRecord(challenge.MapId, challenge.LevelType, challengeCnt);
    }

    private static Task RecordPveChallengeStarAsync(DataConnection database, ulong gameUlongUID, RolePveChallengeStar star, long now) =>
        database.GetTable<RolePveChallengeNode>().InsertOrUpdateAsync(
            () => new RolePveChallengeNode
            {
                GameUlongUID = gameUlongUID,
                StageID = star.StageID,
                NodeID = star.NodeID,
                DifficultyLevel = star.DifficultyLevel,
                StarNum = star.StarNum,
                CompleteTime = now,
            },
            saved => new RolePveChallengeNode
            {
                StarNum = saved.StarNum > star.StarNum ? saved.StarNum : star.StarNum,
            });
}
