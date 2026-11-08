using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_invite_friend")]
public sealed class RoleInviteFriend
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("friend_ulong_uid")]
    public ulong FriendUlongUID { get; set; }

    [Column("bind_time")]
    public long BindTime { get; set; }
}
