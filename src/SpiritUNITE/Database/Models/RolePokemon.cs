using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_pokemon")]
public sealed class RolePokemon
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("pokemon_id")]
    public int PokemonId { get; set; }
}
