using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_pokemon")]
public sealed class RolePokemon
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
}
