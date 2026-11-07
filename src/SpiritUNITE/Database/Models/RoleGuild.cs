using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_guild")]
public sealed class RoleGuild
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("last_quit_time")]
    public long LastQuitTime { get; set; }

    [Column("quit_cnt")]
    public int QuitCnt { get; set; }

    [Column("history_label_1")]
    public int HistoryLabel1 { get; set; }

    [Column("history_label_2")]
    public int HistoryLabel2 { get; set; }

    [Column("history_label_3")]
    public int HistoryLabel3 { get; set; }
}
