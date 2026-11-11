using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_statistic")]
public sealed class RoleStatistic
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("statistic_info")]
    public byte[] StatisticInfo { get; set; } = [];

    [Column("season_id")]
    public int SeasonID { get; set; }

    [Column("season_statistic_info")]
    public byte[] SeasonStatisticInfo { get; set; } = [];
}
