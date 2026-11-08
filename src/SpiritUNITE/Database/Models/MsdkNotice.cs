using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("msdk_notice")]
public sealed class MsdkNotice
{
    [PrimaryKey]
    [Column("notice_id")]
    public int NoticeId { get; set; }

    [Column("notice_type")]
    public int NoticeType { get; set; }

    [Column("notice_group")]
    public string NoticeGroup { get; set; } = string.Empty;

    [Column("begin_time")]
    public long BeginTime { get; set; }

    [Column("end_time")]
    public long EndTime { get; set; }

    [Column("notice_order")]
    public int Order { get; set; }

    [Column("notice_title")]
    public string NoticeTitle { get; set; } = string.Empty;

    [Column("notice_content")]
    public string NoticeContent { get; set; } = string.Empty;
}
