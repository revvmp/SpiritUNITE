using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

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

    [Column("role_avatar_info")]
    public byte[] RoleAvatarInfo { get; set; } = [];

    [Column("newbie_state")]
    public byte[] NewbieState { get; set; } = [];
}
