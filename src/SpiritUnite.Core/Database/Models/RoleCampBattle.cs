using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_camp_battle")]
public sealed class RoleCampBattle
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [Column("camp")]
    public int Camp { get; set; }

    [Column("score")]
    public long Score { get; set; }

    [Column("is_award")]
    public int IsAward { get; set; }

    [Column("join_time")]
    public long JoinTime { get; set; }
}
