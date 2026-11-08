using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("gift_code")]
public sealed class GiftCodeRow
{
    [PrimaryKey]
    [Column("gift_code")]
    public string GiftCode { get; set; } = string.Empty;

    [Column("valid_start_time")]
    public long ValidStartTime { get; set; }

    [Column("valid_end_time")]
    public long ValidEndTime { get; set; }

    [Column("max_use_cnt")]
    public int MaxUseCnt { get; set; }

    [Column("use_cnt")]
    public int UseCnt { get; set; }

    [Column("is_enabled")]
    public bool IsEnabled { get; set; }

    [Column("create_time")]
    public long CreateTime { get; set; }
}
