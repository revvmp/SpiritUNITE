using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_friend_black")]
public sealed class RoleFriendBlack
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("black_uid")]
    public ulong BlackUid { get; set; }

    [Column("add_time")]
    public long AddTime { get; set; }
}
