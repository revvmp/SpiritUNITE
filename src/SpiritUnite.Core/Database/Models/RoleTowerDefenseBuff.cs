using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_tower_defense_buff")]
public sealed class RoleTowerDefenseBuff
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("period_id")]
    public int PeriodID { get; set; }

    [PrimaryKey(2)]
    [Column("buff_id")]
    public int BuffID { get; set; }

    [Column("buff_level")]
    public int BuffLevel { get; set; }

    [Column("attr_id")]
    public int AttrID { get; set; }

    [Column("is_new_unlock")]
    public int IsNewUnLock { get; set; }
}
