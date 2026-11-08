using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("host_activation_code")]
public sealed class HostActivationCodeRow
{
    [PrimaryKey]
    [Column("activation_code")]
    public string ActivationCode { get; set; } = string.Empty;

    [Column("valid_end_time")]
    public long ValidEndTime { get; set; }

    [Column("max_use_cnt")]
    public int MaxUseCnt { get; set; }
}
