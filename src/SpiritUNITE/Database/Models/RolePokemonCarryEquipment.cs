using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_pokemon_carry_equipment")]
public sealed class RolePokemonCarryEquipment
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("pokemon_id")]
    public int PokemonId { get; set; }

    [Column("carry_equipment_plan_id")]
    public int CarryEquipmentPlanID { get; set; }

    [Column("equipment_id_list")]
    public string EquipmentIDList { get; set; } = string.Empty;
}
