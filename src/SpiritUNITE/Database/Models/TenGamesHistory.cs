using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("ten_games_history")]
public sealed class TenGamesHistory
{
    [PrimaryKey(0)]
    [Column("games_id")]
    public int GamesId { get; set; }

    [PrimaryKey(1)]
    [Column("team_id")]
    public ulong TeamID { get; set; }

    [Column("team_name")]
    public string TeamName { get; set; } = "";

    [Column("team_leader_uid")]
    public ulong TeamLeaderUid { get; set; }

    [Column("total_score")]
    public int TotalScore { get; set; }

    [Column("rank_no")]
    public int RankNo { get; set; }
}
