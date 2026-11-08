using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_credit_reduce_record")]
public sealed class RoleCreditReduceRecord
{
    [PrimaryKey]
    [Identity]
    [Column("credit_reduce_record_id")]
    public ulong CreditReduceRecordId { get; set; }

    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("game_start_time")]
    public long GameStartTime { get; set; }

    [Column("map_mode")]
    public int MapMode { get; set; }

    [Column("map_sub_mode")]
    public int MapSubMode { get; set; }

    [Column("reason")]
    public int Reason { get; set; }

    [Column("reduce_point")]
    public int ReducePoint { get; set; }

    [Column("hero_id")]
    public int HeroID { get; set; }

    [Column("desk_uniq_id")]
    public string DeskUniqId { get; set; } = "";
}
