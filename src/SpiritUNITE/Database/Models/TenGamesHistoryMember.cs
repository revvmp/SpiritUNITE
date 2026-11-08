using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("ten_games_history_member")]
public sealed class TenGamesHistoryMember
{
    [PrimaryKey(0)]
    [Column("games_id")]
    public int GamesId { get; set; }

    [PrimaryKey(1)]
    [Column("team_id")]
    public ulong TeamID { get; set; }

    [PrimaryKey(2)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }
}
