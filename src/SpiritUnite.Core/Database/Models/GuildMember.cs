using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("guild_member")]
public sealed class GuildMember
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("guild_id")]
    public ulong GuildID { get; set; }

    [Column("office")]
    public int Office { get; set; }

    [Column("join_time")]
    public long JoinTime { get; set; }

    [Column("comment_last_send_index")]
    public long? CommentLastSendIndex { get; set; }

    [Column("comment_last_read_index")]
    public long? CommentLastReadIndex { get; set; }
}
