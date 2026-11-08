using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_pve_challenge_buff")]
public sealed class RolePveChallengeBuff
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("buff_id")]
    public int BuffID { get; set; }

    [Column("buff_level")]
    public int BuffLevel { get; set; }

    [Column("is_new_unlock")]
    public int IsNewUnLock { get; set; }
}
