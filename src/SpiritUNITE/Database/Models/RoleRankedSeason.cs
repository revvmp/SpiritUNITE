using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_ranked_season")]
public sealed class RoleRankedSeason
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("season_id")]
    public int SeasonId { get; set; }

    [Column("ranked_division")]
    public int RankedDivision { get; set; }

    [Column("win_point")]
    public int WinPoint { get; set; }

    [Column("master_point")]
    public int MasterPoint { get; set; }

    [Column("highest_ranked_division")]
    public int HighestRankedDivision { get; set; }

    [Column("highest_master_point")]
    public int HighestMasterPoint { get; set; }

    [Column("battle_cnt")]
    public int BattleCnt { get; set; }

    [Column("win_cnt")]
    public int WinCnt { get; set; }

    [Column("entry_ranked_division")]
    public int EntryRankedDivision { get; set; }

    [Column("entry_win_point")]
    public int EntryWinPoint { get; set; }

    [Column("entry_master_point")]
    public int EntryMasterPoint { get; set; }

    [Column("entry_ticket_consume")]
    public int EntryTicketConsume { get; set; }

    [Column("use_free_ticket")]
    public bool UseFreeTicket { get; set; }
}
