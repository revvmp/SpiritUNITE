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

    public static async Task<RoleEnergyInfo?> ReadForUpdateAsync(DataConnection database, ulong gameUlongUID)
    {
        var rows = await database.GetTable<RoleEnergy>()
            .Where(energy => energy.GameUlongUID == gameUlongUID)
            .AsMySql()
            .ForUpdateHint()
            .ToArrayAsync();
        return rows.Length == 0 ? null : ToRoleEnergyInfo(rows[0]);
    }

    public static Task SaveAsync(DataConnection database, ulong gameUlongUID, RoleEnergyInfo energy) =>
        database.GetTable<RoleEnergy>().InsertOrUpdateAsync(
            () => new RoleEnergy
            {
                GameUlongUID = gameUlongUID,
                Energy = energy.Energy,
                EnergyFreeCapacity = energy.EnergyFreeCapacity,
                NextFlushTime = energy.NextFlushTime,
            },
            _ => new RoleEnergy
            {
                Energy = energy.Energy,
                EnergyFreeCapacity = energy.EnergyFreeCapacity,
                NextFlushTime = energy.NextFlushTime,
            });

    private static RoleEnergyInfo ToRoleEnergyInfo(RoleEnergy row) => new(row.Energy, row.EnergyFreeCapacity, row.NextFlushTime);
}
