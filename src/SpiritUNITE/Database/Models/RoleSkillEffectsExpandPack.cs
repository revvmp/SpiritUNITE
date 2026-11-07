using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_skill_effects_expand_pack")]
public sealed class RoleSkillEffectsExpandPack
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("pokemon_avatar_id")]
    public int PokemonAvatarId { get; set; }

    [PrimaryKey(2)]
    [Column("skill_effects_expand_pack_type")]
    public int SkillEffectsExpandPackType { get; set; }

    [Column("skill_effects_expand_pack_id")]
    public int SkillEffectsExpandPackID { get; set; }
}
