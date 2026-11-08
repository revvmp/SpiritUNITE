using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_friend_request")]
public sealed class RoleFriendRequest
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("request_uid")]
    public ulong RequestUid { get; set; }

    [Column("from_type")]
    public int FromType { get; set; }

    [Column("self_camp")]
    public int SelfCamp { get; set; }

    [Column("request_time")]
    public long RequestTime { get; set; }
}
