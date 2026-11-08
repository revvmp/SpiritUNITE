using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_chat_offline")]
public sealed class RoleChatOffline
{
    [PrimaryKey]
    [Identity]
    [Column("chat_id")]
    public ulong ChatId { get; set; }

    [Column("dest_uid")]
    public ulong DestUid { get; set; }

    [Column("from_uid")]
    public ulong FromUid { get; set; }

    [Column("chat_time")]
    public long ChatTime { get; set; }

    [Column("chat_msg")]
    public byte[] ChatMsg { get; set; } = [];
}
