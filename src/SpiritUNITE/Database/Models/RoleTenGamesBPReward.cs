using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_ten_games_bp_reward")]
public sealed class RoleTenGamesBPReward
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

    [PrimaryKey(3)]
    [Column("receive_reward_type")]
    public int ReceiveRewardType { get; set; }

    [Column("reward_time")]
    public long RewardTime { get; set; }
}
