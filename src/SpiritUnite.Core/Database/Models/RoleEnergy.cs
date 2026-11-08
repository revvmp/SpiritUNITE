using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_energy")]
public sealed class RoleEnergy
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("energy")]
    public int Energy { get; set; }

    [Column("energy_free_capacity")]
    public int EnergyFreeCapacity { get; set; }

    [Column("next_flush_time")]
    public long NextFlushTime { get; set; }
}
