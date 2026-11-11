using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Energy;

namespace SpiritUnite.Database.Repositories;

public sealed class EnergyRepository(DataOptions options)
{
    public async Task<RoleEnergyInfo?> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleEnergy>().FirstOrDefaultAsync(energy => energy.GameUlongUID == gameUlongUID);
        return row is null ? null : ToRoleEnergyInfo(row);
    }

    public static async Task<RoleEnergyInfo> ReadOrCreateForUpdateAsync(DataConnection database, ulong gameUlongUID, RoleEnergyInfo starting)
    {
        await database.GetTable<RoleEnergy>().InsertOrUpdateAsync(
            () => new RoleEnergy
            {
                GameUlongUID = gameUlongUID,
                Energy = starting.Energy,
                EnergyFreeCapacity = starting.EnergyFreeCapacity,
                NextFlushTime = starting.NextFlushTime,
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
        return ToRoleEnergyInfo(rows.Single());
    }

    public static Task SaveAsync(DataConnection database, ulong gameUlongUID, RoleEnergyInfo energy) =>
        database.GetTable<RoleEnergy>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .Set(row => row.Energy, energy.Energy)
            .Set(row => row.EnergyFreeCapacity, energy.EnergyFreeCapacity)
            .Set(row => row.NextFlushTime, energy.NextFlushTime)
            .UpdateAsync();

    private static RoleEnergyInfo ToRoleEnergyInfo(RoleEnergy row) => new(row.Energy, row.EnergyFreeCapacity, row.NextFlushTime);
}
