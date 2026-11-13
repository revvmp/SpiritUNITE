using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_item")]
public sealed class RoleItem
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("item_type")]
    public int ItemType { get; set; }

    [PrimaryKey(2)]
    [Column("item_id")]
    public int ItemId { get; set; }

    [Column("gain_time")]
    public long GainTime { get; set; }

    [Column("time_limited")]
    public long TimeLimited { get; set; }

    [Column("new_tag")]
    public int NewTag { get; set; }

    [Column("item_cnt")]
    public int ItemCnt { get; set; } = 1;

    [Column("item_level")]
    public int ItemLevel { get; set; }

    [Column("held_item_experience_level")]
    public int HeldItemExperienceLevel { get; set; }

    [Column("held_item_experience_expire_time")]
    public long HeldItemExperienceExpireTime { get; set; }

    [Column("grid_unique_id")]
    public ulong? GridUniqueID { get; set; }
}
