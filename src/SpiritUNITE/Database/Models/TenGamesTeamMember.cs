using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("ten_games_team_member")]
public sealed class TenGamesTeamMember
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("team_id")]
    public ulong TeamID { get; set; }

    [Column("join_time")]
    public long JoinTime { get; set; }
}
