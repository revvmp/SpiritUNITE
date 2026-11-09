using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_friend")]
public sealed class RoleFriend
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("friend_uid")]
    public ulong FriendUid { get; set; }

    [Column("add_friend_time")]
    public long AddFriendTime { get; set; }

    [Column("ban_chat_flag")]
    public int BanChatFlag { get; set; }

    [Column("intimacy_value")]
    public int IntimacyValue { get; set; }

    [Column("together_battle_cnt")]
    public int TogetherBattleCnt { get; set; }
}
