using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("ten_games_team")]
public sealed class TenGamesTeam
{
    [PrimaryKey]
    [Identity]
    [Column("team_id")]
    public ulong TeamID { get; set; }

    [Column("team_name")]
    public string TeamName { get; set; } = "";

    [Column("team_leader_uid")]
    public ulong TeamLeaderUid { get; set; }

    [Column("join_ten_games_id")]
    public int JoinTenGamesID { get; set; }

    [Column("cur_score")]
    public int CurScore { get; set; }

    [Column("win_count")]
    public int WinCount { get; set; }

    [Column("lose_count")]
    public int LoseCount { get; set; }

    [Column("modify_name_time")]
    public long ModifyNameTime { get; set; }

    [Column("create_time")]
    public long CreateTime { get; set; }
}
