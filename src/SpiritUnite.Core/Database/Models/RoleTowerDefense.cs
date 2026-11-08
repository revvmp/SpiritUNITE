using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_tower_defense")]
public sealed class RoleTowerDefense
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("period_id")]
    public int PeriodID { get; set; }

    [Column("score")]
    public int Score { get; set; }

    [Column("max_pass_game_level")]
    public int MaxPassGameLevel { get; set; }

    [Column("is_new_unlock")]
    public int IsNewUnLock { get; set; }
}
