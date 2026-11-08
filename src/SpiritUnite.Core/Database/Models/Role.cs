using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role")]
public sealed class Role
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("in_game_usr_name")]
    public string InGameUsrName { get; set; } = string.Empty;

    [Column("gender")]
    public int Gender { get; set; }

    [Column("role_total_exp")]
    public int RoleTotalExp { get; set; }

    [Column("role_history_max_total_exp")]
    public int RoleHistoryMaxTotalExp { get; set; }

    [Column("role_avatar_info")]
    public byte[] RoleAvatarInfo { get; set; } = [];

    [Column("newbie_state")]
    public byte[] NewbieState { get; set; } = [];

    [Column("follow_pokemon_id")]
    public int FollowPokemonId { get; set; }

    [Column("using_lobby_bg_id")]
    public int UsingLobbyBgId { get; set; }

    [Column("first_pokemon_id")]
    public int FirstPokemonId { get; set; }
}
