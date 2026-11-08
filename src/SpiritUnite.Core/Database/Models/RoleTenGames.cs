using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_ten_games")]
public sealed class RoleTenGames
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("ten_games_team_create_time")]
    public long TenGamesTeamCreateTime { get; set; }

    [Column("ten_games_last_read_invite_time")]
    public long TenGamesLastReadInviteTime { get; set; }
}
