using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_pokemon_time_limited")]
public sealed class RolePokemonTimeLimited
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("pokemon_id")]
    public int PokemonId { get; set; }

    [Column("gain_time")]
    public long GainTime { get; set; }

    [Column("gain_channel")]
    public int GainChannel { get; set; }

    [Column("new_tag")]
    public int NewTag { get; set; }

    [Column("time_limited")]
    public long TimeLimited { get; set; }
}
