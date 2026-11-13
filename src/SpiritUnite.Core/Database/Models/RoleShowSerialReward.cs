using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_show_serial_reward")]
public sealed class RoleShowSerialReward
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [PrimaryKey(2)]
    [Column("reward_seq")]
    public int RewardSeq { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }

    [Column("mailed")]
    public bool Mailed { get; set; }
}
