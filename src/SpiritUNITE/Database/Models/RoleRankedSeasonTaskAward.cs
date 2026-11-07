using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_ranked_season_task_award")]
public sealed class RoleRankedSeasonTaskAward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("season_id")]
    public int SeasonId { get; set; }

    [PrimaryKey(2)]
    [Column("ranked_division")]
    public int RankedDivision { get; set; }
}
