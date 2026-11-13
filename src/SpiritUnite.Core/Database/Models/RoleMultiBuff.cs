using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_multi_buff")]
public sealed class RoleMultiBuff
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("multi_buff_type")]
    public int MultiBuffType { get; set; }

    [Column("multi_buff_sub_type")]
    public int MultiBuffSubType { get; set; }

    [Column("multiple_value")]
    public int MultipleValue { get; set; }

    [Column("last_use_multi_buff_res_id")]
    public int LastUseMultiBuffResID { get; set; }

    [Column("time_type_buf_end_time_stamp")]
    public long TimeTypeBufEndTimeStamp { get; set; }
}
