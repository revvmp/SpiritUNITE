using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_mail")]
public sealed class RoleMail
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("mail_index")]
    public int MailIndex { get; set; }

    [Column("mail_type")]
    public int MailType { get; set; }

    [Column("mail_sub_type")]
    public int MailSubType { get; set; }

    [Column("mail_id")]
    public int MailID { get; set; }

    [Column("mail_state")]
    public int MailState { get; set; }

    [Column("from_uid")]
    public ulong FromUid { get; set; }

    [Column("from_name")]
    public string FromName { get; set; } = string.Empty;

    [Column("subject")]
    public string Subject { get; set; } = string.Empty;

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("content_param")]
    public byte[] ContentParam { get; set; } = [];

    [Column("send_time")]
    public long SendTime { get; set; }

    [Column("expire_time")]
    public long ExpireTime { get; set; }

    [Column("access_info")]
    public byte[] AccessInfo { get; set; } = [];

    [Column("access_state")]
    public int AccessState { get; set; }
}
