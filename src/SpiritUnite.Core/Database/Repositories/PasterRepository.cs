using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record PasterSprayCommit(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<BagItem> ChangedItems, PasterSprayInfo? Spray)
{
    public static PasterSprayCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, [], null);
}

public sealed class PasterRepository(DataOptions options)
{
    private const int NoNewTag = 0;

    public async Task<IReadOnlyList<RoleItemInfo>> LoadTimeLimitedAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RolePasterTimeLimited>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .OrderBy(row => row.PasterId)
            .ToArrayAsync();
        return [.. rows.Select(ToRoleItemInfo)];
    }

    public async Task<bool> ClearTimeLimitedNewTagAsync(ulong gameUlongUID, int pasterId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RolePasterTimeLimited>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PasterId == pasterId)
            .Set(row => row.NewTag, NoNewTag)
            .UpdateAsync() > 0;
    }

    public async Task<IReadOnlyList<PasterSprayInfo>> LoadSpraysAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RolePasterSpray>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .OrderBy(row => row.PasterId)
            .ThenBy(row => row.PasterSprayID)
            .ToArrayAsync();
        return [.. rows.Select(ToPasterSprayInfo)];
    }

    public async Task<PasterSprayCommit> UseSprayerAsync(
        ulong gameUlongUID,
        int pasterId,
        int pasterSprayID,
        IReadOnlyList<BagCost> costs,
        long durationSec,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var debited = await BagRepository.ApplyAsync(database, gameUlongUID, new BagChange { Costs = costs }, now);
        if (debited != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return PasterSprayCommit.Refused(debited);
        }

        var sprays = database.GetTable<RolePasterSpray>();
        var current = await sprays.FirstOrDefaultAsync(row =>
            row.GameUlongUID == gameUlongUID && row.PasterId == pasterId && row.PasterSprayID == pasterSprayID);
        var timeLimited = checked(Math.Max(current?.TimeLimited ?? now, now) + durationSec);
        if (current is null)
        {
            await database.InsertAsync(new RolePasterSpray
            {
                GameUlongUID = gameUlongUID,
                PasterId = pasterId,
                PasterSprayID = pasterSprayID,
                TimeLimited = timeLimited,
            });
        }
        else
        {
            await sprays
                .Where(row => row.GameUlongUID == gameUlongUID && row.PasterId == pasterId && row.PasterSprayID == pasterSprayID)
                .Set(row => row.TimeLimited, timeLimited)
                .UpdateAsync();
        }

        var changedItems = await BagRepository.ReadItemsAsync(
            database,
            gameUlongUID,
            costs.Select(cost => (cost.ItemType, cost.ItemId)).Distinct());
        await transaction.CommitAsync();
        return new PasterSprayCommit(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            changedItems,
            new PasterSprayInfo(pasterId, pasterSprayID, timeLimited, current?.IsWearing ?? false));
    }

    public async Task WearSprayAsync(ulong gameUlongUID, int pasterId, int pasterSprayID)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RolePasterSpray>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PasterId == pasterId)
            .Set(row => row.IsWearing, row => row.PasterSprayID == pasterSprayID)
            .UpdateAsync();
    }

    public async Task<IReadOnlyDictionary<(int PokemonAvatarId, int SkillEffectsExpandPackType), int>> LoadUsingSkillEffectsExpandPacksAsync(
        ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleSkillEffectsExpandPack>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        return rows.ToDictionary(row => (row.PokemonAvatarId, row.SkillEffectsExpandPackType), row => row.SkillEffectsExpandPackID);
    }

    public async Task UseSkillEffectsExpandPackAsync(
        ulong gameUlongUID,
        int pokemonAvatarId,
        int skillEffectsExpandPackType,
        int skillEffectsExpandPackID)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(new RoleSkillEffectsExpandPack
        {
            GameUlongUID = gameUlongUID,
            PokemonAvatarId = pokemonAvatarId,
            SkillEffectsExpandPackType = skillEffectsExpandPackType,
            SkillEffectsExpandPackID = skillEffectsExpandPackID,
        });
    }

    internal static async Task ApplyTimeLimitedAsync(
        DataConnection database,
        ulong gameUlongUID,
        IEnumerable<PasterTimeLimit> grants,
        long now)
    {
        var table = database.GetTable<RolePasterTimeLimited>();
        foreach (var grant in grants)
        {
            var current = await table.FirstOrDefaultAsync(row => row.GameUlongUID == gameUlongUID && row.PasterId == grant.PasterId);
            var timeLimited = grant.ExtendFrom(current?.TimeLimited ?? now, now);
            if (current is null)
            {
                await database.InsertAsync(new RolePasterTimeLimited
                {
                    GameUlongUID = gameUlongUID,
                    PasterId = grant.PasterId,
                    GainTime = now,
                    NewTag = RoleItemInfo.ShowNewTag,
                    TimeLimited = timeLimited,
                });
                continue;
            }

            await table
                .Where(row => row.GameUlongUID == gameUlongUID && row.PasterId == grant.PasterId)
                .Set(row => row.TimeLimited, timeLimited)
                .Set(row => row.GainTime, now)
                .Set(row => row.NewTag, RoleItemInfo.ShowNewTag)
                .UpdateAsync();
        }
    }

    internal static async Task<IReadOnlyList<RoleItemInfo>> ReadTimeLimitedAsync(
        DataConnection database,
        ulong gameUlongUID,
        IEnumerable<int> pasterIds)
    {
        int[] ids = [.. pasterIds.Distinct()];
        if (ids.Length == 0)
        {
            return [];
        }

        var rows = await database.GetTable<RolePasterTimeLimited>()
            .Where(row => row.GameUlongUID == gameUlongUID && ids.Contains(row.PasterId))
            .ToArrayAsync();
        return [.. rows.Select(ToRoleItemInfo)];
    }

    private static RoleItemInfo ToRoleItemInfo(RolePasterTimeLimited row) =>
        new(RoleItemType.Paster, row.PasterId, row.GainTime, row.TimeLimited, row.NewTag);

    private static PasterSprayInfo ToPasterSprayInfo(RolePasterSpray row) =>
        new(row.PasterId, row.PasterSprayID, row.TimeLimited, row.IsWearing);
}
