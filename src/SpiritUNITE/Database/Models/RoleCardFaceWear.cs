using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_card_face_wear")]
public sealed class RoleCardFaceWear
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("pokemon_id")]
    public int PokemonID { get; set; }

    [Column("card_face_id")]
    public int CardFaceID { get; set; }
}
