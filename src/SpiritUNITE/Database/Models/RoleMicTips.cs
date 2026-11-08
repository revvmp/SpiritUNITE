using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_mic_tips")]
public sealed class RoleMicTips
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("show_mic_tips_time")]
    public long ShowMicTipsTime { get; set; }

    [Column("show_mic_tips_cnt")]
    public int ShowMicTipsCnt { get; set; }
}
