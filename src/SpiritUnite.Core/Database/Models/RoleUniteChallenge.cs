using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_unite_challenge")]
public sealed class RoleUniteChallenge
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("period_id")]
    public int PeriodId { get; set; }

    [Column("is_click_start")]
    public int IsClickStart { get; set; }

    [Column("click_start_time")]
    public long ClickStartTime { get; set; }

    [Column("round_cnt")]
    public int RoundCnt { get; set; }
}
