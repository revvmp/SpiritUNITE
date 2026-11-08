using System.Globalization;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed class CarryEquipmentRepository(DataOptions options)
{
    private const char EquipmentIDSeparator = ',';

    public async Task<IReadOnlyDictionary<int, pbCOMDT_CARRYEQUIPMENT_PLAN_INFO>> LoadAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RolePokemonCarryEquipment>()
            .Where(plan => plan.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        return rows.ToDictionary(row => row.PokemonId, ToPlanInfo);
    }

    public async Task SaveAsync(ulong gameUlongUID, int pokemonId, pbCOMDT_CARRYEQUIPMENT_PLAN_INFO plan)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(new RolePokemonCarryEquipment
        {
            GameUlongUID = gameUlongUID,
            PokemonId = pokemonId,
            CarryEquipmentPlanID = plan.CarryEquipmentPlanID,
            EquipmentIDList = string.Join(EquipmentIDSeparator, plan.EquipmentID.Select(id => id.ToString(CultureInfo.InvariantCulture))),
        });
    }

    private static pbCOMDT_CARRYEQUIPMENT_PLAN_INFO ToPlanInfo(RolePokemonCarryEquipment row) => new()
    {
        CarryEquipmentPlanID = row.CarryEquipmentPlanID,
        EquipmentID = { row.EquipmentIDList.Split(EquipmentIDSeparator).Select(id => int.Parse(id, CultureInfo.InvariantCulture)) },
    };
}
