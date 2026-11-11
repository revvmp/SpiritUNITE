using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_credit_recovery_day")]
public sealed class RoleCreditRecoveryDay
{
    [PrimaryKey(0), Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1), Column("day")]
    public long Day { get; set; }

    [Column("resume_credit")]
    public int ResumeCredit { get; set; }
}
