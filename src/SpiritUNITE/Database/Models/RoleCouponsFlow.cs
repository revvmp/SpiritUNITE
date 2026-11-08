using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_coupons_flow")]
public sealed class RoleCouponsFlow
{
    [PrimaryKey]
    [Identity]
    [Column("flow_id")]
    public ulong FlowID { get; set; }

    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("num")]
    public int Num { get; set; }

    [Column("flow_time")]
    public long FlowTime { get; set; }

    [Column("system_type")]
    public int SystemType { get; set; }

    [Column("extra_info")]
    public byte[] ExtraInfo { get; set; } = [];
}
