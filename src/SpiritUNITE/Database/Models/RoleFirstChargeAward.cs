using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_first_charge_award")]
public sealed class RoleFirstChargeAward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("login_seq")]
    public int LoginSeq { get; set; }

    [Column("award_time")]
    public long AwardTime { get; set; }
}
