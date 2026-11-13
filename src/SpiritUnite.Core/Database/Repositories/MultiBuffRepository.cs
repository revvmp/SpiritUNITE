using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed class MultiBuffRepository(DataOptions options)
{
    private const int OneRow = 1;

    public async Task<IReadOnlyList<MultiBuff>> LoadAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await ReadAsync(database, gameUlongUID);
    }

    public async Task<MultiBuffUse> UseCardAsync(ulong gameUlongUID, BagCost cost, MultiBuffCard card, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return MultiBuffUse.Refused(PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemNotFoundResInAcnt);
        }

        var debited = await BagRepository.ApplyAsync(database, gameUlongUID, new BagChange { Costs = [cost] }, now);
        if (debited != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return MultiBuffUse.Refused(debited);
        }

        var current = (await ReadAsync(database, gameUlongUID)).FirstOrDefault(buff => buff.MultiBuffType == card.MultiBuffType);
        await SaveAsync(database, gameUlongUID, card.ApplyTo(current, cost.Cnt, now), exists: current is not null);
        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, [(RoleItemType.OutSideItem, cost.ItemId)]);
        var buffs = await ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new MultiBuffUse(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, changedItems, buffs, now);
    }

    private static async Task SaveAsync(DataConnection database, ulong gameUlongUID, MultiBuff buff, bool exists)
    {
        if (exists)
        {
            await database.GetTable<RoleMultiBuff>()
                .Where(row => row.GameUlongUID == gameUlongUID && row.MultiBuffType == (int)buff.MultiBuffType)
                .Set(row => row.MultiBuffSubType, (int)buff.MultiBuffSubType)
                .Set(row => row.MultipleValue, buff.MultipleValue)
                .Set(row => row.LastUseMultiBuffResID, buff.LastUseMultiBuffResID)
                .Set(row => row.TimeTypeBufEndTimeStamp, buff.TimeTypeBufEndTimeStamp)
                .UpdateAsync();
            return;
        }

        await database.InsertAsync(new RoleMultiBuff
        {
            GameUlongUID = gameUlongUID,
            MultiBuffType = (int)buff.MultiBuffType,
            MultiBuffSubType = (int)buff.MultiBuffSubType,
            MultipleValue = buff.MultipleValue,
            LastUseMultiBuffResID = buff.LastUseMultiBuffResID,
            TimeTypeBufEndTimeStamp = buff.TimeTypeBufEndTimeStamp,
        });
    }

    private static async Task<IReadOnlyList<MultiBuff>> ReadAsync(DataConnection database, ulong gameUlongUID)
    {
        var rows = await database.GetTable<RoleMultiBuff>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .OrderBy(row => row.MultiBuffType)
            .ToArrayAsync();
        return
        [
            .. rows.Select(row => new MultiBuff(
                (PB_ENUM_RES_MULTIBUFF_TYPE)row.MultiBuffType,
                (PB_ENUM_RES_MULTIBUFF_SUBTYPE)row.MultiBuffSubType,
                row.MultipleValue,
                row.LastUseMultiBuffResID,
                row.TimeTypeBufEndTimeStamp)),
        ];
    }

    private static async Task<bool> LockRoleAsync(DataConnection database, ulong gameUlongUID) =>
        (await database.GetTable<Role>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .Select(role => role.GameUlongUID)
            .ToArrayAsync()).Length == OneRow;
}
