using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_video_history_tip")]
public sealed class RoleVideoHistoryTip
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("del_video_guide_tip")]
    public int DelVideoGuideTip { get; set; }

    [Column("video_del_by_version")]
    public int VideoDelByVersion { get; set; }
}
