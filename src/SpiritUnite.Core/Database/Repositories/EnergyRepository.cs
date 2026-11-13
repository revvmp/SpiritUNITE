using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Energy;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record EnergyDrawCommit(PB_ENUM_RES_ERROR_CODE Result, RoleEnergyInfo Energy, BagCommit? Bag)
{
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
}

public sealed record EnergyChangeCommit(PB_ENUM_RES_ERROR_CODE Result, RoleEnergyInfo Energy, BagCommit? Bag)
{
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
}

public sealed class EnergyRepository(DataOptions options)
{
    public async Task<RoleEnergyInfo?> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleEnergy>().FirstOrDefaultAsync(energy => energy.GameUlongUID == gameUlongUID);
        return row is null ? null : ToRoleEnergyInfo(row, await ReadAcceleratorsAsync(database, gameUlongUID));
    }

    public async Task<IReadOnlyList<EnergyAccelerator>> FindAcceleratorsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await ReadAcceleratorsAsync(database, gameUlongUID);
    }

    public async Task<EnergyDrawCommit> DrawAsync(
        ulong gameUlongUID,
        RoleEnergyInfo starting,
        Func<RoleEnergyInfo, (PB_ENUM_RES_ERROR_CODE Result, RoleEnergyInfo After)> charge,
        BagChange rewards,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var stored = await ReadOrCreateForUpdateAsync(database, gameUlongUID, starting);
        var (result, after) = charge(stored);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return new EnergyDrawCommit(result, after, null);
        }

        var bag = await BagRepository.ApplyAndReadAsync(database, gameUlongUID, rewards, now);
        if (bag.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return new EnergyDrawCommit(bag.Result, stored, null);
        }

        await SaveAsync(database, gameUlongUID, stored, after);
        await transaction.CommitAsync();
        return new EnergyDrawCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, after, bag);
    }

    public async Task<EnergyChangeCommit> ChangeAsync(
        ulong gameUlongUID,
        RoleEnergyInfo starting,
        Func<RoleEnergyInfo, (PB_ENUM_RES_ERROR_CODE Result, RoleEnergyInfo After)> change,
        BagChange cost,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var bag = await BagRepository.ApplyAndReadAsync(database, gameUlongUID, cost, now);
        if (bag.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return new EnergyChangeCommit(bag.Result, starting, null);
        }

        var stored = await ReadOrCreateForUpdateAsync(database, gameUlongUID, starting);
        var (result, after) = change(stored);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return new EnergyChangeCommit(result, stored, null);
        }

        await SaveAsync(database, gameUlongUID, stored, after);
        await transaction.CommitAsync();
        return new EnergyChangeCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, after, bag);
    }

    public static async Task<RoleEnergyInfo> ReadOrCreateForUpdateAsync(DataConnection database, ulong gameUlongUID, RoleEnergyInfo starting)
    {
        await database.GetTable<RoleEnergy>().InsertOrUpdateAsync(
            () => new RoleEnergy
            {
                GameUlongUID = gameUlongUID,
                Energy = starting.Energy,
                RaffleTicket = starting.RaffleTicket,
                EnergyFreeCapacity = starting.EnergyFreeCapacity,
                NextFlushTime = starting.NextFlushTime,
                TodayDrawNum = starting.TodayDrawNum,
                DailyFreeDrawUseNum = starting.DailyFreeDrawUseNum,
                NextDrawResetTime = starting.NextDrawResetTime,
                EnergyPayCapacity = starting.EnergyPayCapacity,
                EnergyAccelerateRatio = starting.EnergyAccelerateRatio,
            },
            saved => new RoleEnergy
            {
                Energy = saved.Energy,
            });
        var rows = await database.GetTable<RoleEnergy>()
            .Where(energy => energy.GameUlongUID == gameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .ToArrayAsync();
        var row = rows.Single();
        if (row.EnergyAccelerateRatio != RoleEnergyInfo.NoEnergyAccelerateRatio)
        {
            await database.GetTable<RoleEnergyAccelerate>()
                .Where(accelerator => accelerator.GameUlongUID == gameUlongUID && accelerator.EnergyAccelerateRatio == row.EnergyAccelerateRatio)
                .AsMySql()
                .ForUpdateHint()
                .ToArrayAsync();
        }

        return ToRoleEnergyInfo(row, await ReadAcceleratorsAsync(database, gameUlongUID));
    }

    public static async Task SaveAsync(DataConnection database, ulong gameUlongUID, RoleEnergyInfo stored, RoleEnergyInfo energy)
    {
        await database.GetTable<RoleEnergy>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .Set(row => row.Energy, energy.Energy)
            .Set(row => row.RaffleTicket, energy.RaffleTicket)
            .Set(row => row.EnergyFreeCapacity, energy.EnergyFreeCapacity)
            .Set(row => row.NextFlushTime, energy.NextFlushTime)
            .Set(row => row.TodayDrawNum, energy.TodayDrawNum)
            .Set(row => row.DailyFreeDrawUseNum, energy.DailyFreeDrawUseNum)
            .Set(row => row.NextDrawResetTime, energy.NextDrawResetTime)
            .Set(row => row.EnergyPayCapacity, energy.EnergyPayCapacity)
            .Set(row => row.EnergyAccelerateRatio, energy.EnergyAccelerateRatio)
            .UpdateAsync();
        foreach (var before in stored.Accelerators)
        {
            var spent = before.EnergyAccelerateCapacity - CapacityOf(energy, before.EnergyAccelerateRatio);
            if (spent == 0)
            {
                continue;
            }

            await database.GetTable<RoleEnergyAccelerate>()
                .Where(row => row.GameUlongUID == gameUlongUID && row.EnergyAccelerateRatio == before.EnergyAccelerateRatio)
                .Set(row => row.EnergyAccelerateCapacity, row => row.EnergyAccelerateCapacity - spent)
                .UpdateAsync();
            await database.GetTable<RoleEnergyAccelerate>()
                .Where(row => row.GameUlongUID == gameUlongUID && row.EnergyAccelerateRatio == before.EnergyAccelerateRatio && row.EnergyAccelerateCapacity <= 0)
                .DeleteAsync();
        }
    }

    private static int CapacityOf(RoleEnergyInfo energy, int energyAccelerateRatio) =>
        energy.Accelerators.FirstOrDefault(accelerator => accelerator.EnergyAccelerateRatio == energyAccelerateRatio)?.EnergyAccelerateCapacity ?? 0;

    public static Task AddAcceleratorAsync(DataConnection database, ulong gameUlongUID, int energyAccelerateRatio, int energyAccelerateCapacity) =>
        database.ExecuteAsync(
            "INSERT INTO role_energy_accelerate (game_ulong_uid, energy_accelerate_ratio, energy_accelerate_capacity) VALUES (@uid, @ratio, @capacity) "
                + "ON DUPLICATE KEY UPDATE energy_accelerate_capacity = energy_accelerate_capacity + VALUES(energy_accelerate_capacity)",
            new DataParameter("uid", gameUlongUID),
            new DataParameter("ratio", energyAccelerateRatio),
            new DataParameter("capacity", energyAccelerateCapacity));

    private static async Task<IReadOnlyList<EnergyAccelerator>> ReadAcceleratorsAsync(DataConnection database, ulong gameUlongUID) =>
        await database.GetTable<RoleEnergyAccelerate>()
            .Where(accelerator => accelerator.GameUlongUID == gameUlongUID)
            .OrderBy(accelerator => accelerator.EnergyAccelerateRatio)
            .Select(accelerator => new EnergyAccelerator(accelerator.EnergyAccelerateRatio, accelerator.EnergyAccelerateCapacity))
            .ToArrayAsync();

    private static RoleEnergyInfo ToRoleEnergyInfo(RoleEnergy row, IReadOnlyList<EnergyAccelerator> accelerators) => new(
        row.Energy,
        row.RaffleTicket,
        row.EnergyFreeCapacity,
        row.NextFlushTime,
        row.TodayDrawNum,
        row.DailyFreeDrawUseNum,
        row.NextDrawResetTime)
    {
        EnergyPayCapacity = row.EnergyPayCapacity,
        EnergyAccelerateRatio = row.EnergyAccelerateRatio,
        Accelerators = accelerators,
    };
}
