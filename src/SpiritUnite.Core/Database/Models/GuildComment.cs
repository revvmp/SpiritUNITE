using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("guild_comment")]
public sealed class GuildComment
{
    [PrimaryKey(0)]
    [Column("guild_id")]
    public ulong GuildID { get; set; }

    [PrimaryKey(1)]
    [Column("comment_index")]
    public long CommentIndex { get; set; }

    [Column("comment_type")]
    public int CommentType { get; set; }

    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("member_name")]
    public string MemberName { get; set; } = "";

    [Column("other_uid")]
    public ulong OtherUid { get; set; }

    [Column("other_name")]
    public string OtherName { get; set; } = "";

    [Column("content")]
    public string Content { get; set; } = "";

    [Column("text_id")]
    public int TextID { get; set; }

    [Column("comment_time")]
    public long CommentTime { get; set; }
}
