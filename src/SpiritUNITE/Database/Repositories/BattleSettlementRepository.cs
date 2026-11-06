using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

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
    int MaxFightHistoryNumPerMapSubMode);

public sealed record ChallengeTrainerMapLevel(int MapId, int LevelType);

public sealed record BattleSettlementCommit(
    IReadOnlyList<BagItem> ChangedItems,
    IReadOnlyList<BagGrant> ClampedGrants,
    IReadOnlyDictionary<int, long> BeforeCoinNums,
    RoleWallet Wallet,
    UsedPokemonInfo? UsedPokemon,
    ChallengeTrainerRecord? HeavenTrainerRecord);

public sealed class BattleSettlementRepository(DataOptions options)
{
    private const int OneRow = 1;
    private const int NoPokemon = 0;
    private const int NoStar = 0;
    private const int OneChallenge = 1;

    public async Task<BattleSettlementCommit?> CommitAsync(
        ulong gameUlongUID,
        PveChallengeNodeKey? node,
        Func<int, Task<BattleSettlementChange>> changeAfterStoredStarNum,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return null;
        }

        var storedStarNum = node is null ? NoStar : await ReadStarNumForUpdateAsync(database, gameUlongUID, node);
        var change = await changeAfterStoredStarNum(storedStarNum);
        if (await AddRoleTotalExpAsync(database, gameUlongUID, change.ChgExp) != OneRow)
        {
            await transaction.RollbackAsync();
            return null;
        }

        var usedPokemon = change.PokemonId == NoPokemon
            ? null
            : await AddPokemonPointAsync(database, gameUlongUID, change);
        var clampedGrants = await ApplyGrantsUpToStackAsync(database, gameUlongUID, change.Grants, now);
        if (clampedGrants is null)
        {
            await transaction.RollbackAsync();
            return null;
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var (coinID, amount) in change.Coins)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
            await WalletRepository.CreditAsync(database, gameUlongUID, coinID, amount);
        }

        if (change.PveChallengeStar is { } star && star.StarNum > storedStarNum)
        {
            await RecordPveChallengeStarAsync(database, gameUlongUID, star, now);
        }

        var heavenTrainerRecord = change.HeavenTrainerChallenge is { } challenge
            ? await AddChallengeCntAsync(database, gameUlongUID, challenge)
            : null;
        await FightHistoryRepository.AddAsync(database, change.FightHistory, change.MaxFightHistoryNumPerMapSubMode);
        var changedItems = await BagRepository.ReadItemsAsync(
            database,
            gameUlongUID,
            change.Grants.Select(grant => (grant.ItemType, grant.ItemId)).Distinct());
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new BattleSettlementCommit(changedItems, clampedGrants, beforeCoinNums, wallet, usedPokemon, heavenTrainerRecord);
    }

    private static async Task<bool> LockRoleAsync(DataConnection database, ulong gameUlongUID) =>
        (await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .Select(role => role.GameUlongUID)
            .ToArrayAsync()).Length == OneRow;

    private static async Task<int> ReadStarNumForUpdateAsync(DataConnection database, ulong gameUlongUID, PveChallengeNodeKey node)
    {
        var starNums = await database.GetTable<RolePveChallengeNode>()
            .Where(row => row.GameUlongUID == gameUlongUID
                && row.StageID == node.StageID
                && row.NodeID == node.NodeID
                && row.DifficultyLevel == node.DifficultyLevel)
            .AsMySql()
            .ForUpdateHint()
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

    private static async Task<UsedPokemonInfo> AddPokemonPointAsync(DataConnection database, ulong gameUlongUID, BattleSettlementChange change)
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
            },
            saved => new RolePokemonUsed
            {
                PokemonTotalPoint = saved.PokemonTotalPoint + point,
                PokemonSeasonPoint = saved.PokemonSeasonPoint + point,
                LastBattleWinTime = winTime == UsedPokemonInfo.NeverWon ? saved.LastBattleWinTime : winTime,
            });
        var row = await database.GetTable<RolePokemonUsed>()
            .FirstAsync(used => used.GameUlongUID == gameUlongUID && used.PokemonId == change.PokemonId);
        return PokemonUsedRepository.ToUsedPokemonInfo(row);
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
