using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_energy_accelerate")]
public sealed class RoleEnergyAccelerate
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("energy_accelerate_ratio")]
    public int EnergyAccelerateRatio { get; set; }

    [Column("energy_accelerate_capacity")]
    public int EnergyAccelerateCapacity { get; set; }
}
