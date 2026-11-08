using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_trainer_exp_gift")]
public sealed class RoleTrainerExpGift
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("gift_type")]
    public int GiftType { get; set; }

    [PrimaryKey(2)]
    [Column("reward_exp")]
    public int RewardExp { get; set; }

    [Column("award_time")]
    public long AwardTime { get; set; }
}
