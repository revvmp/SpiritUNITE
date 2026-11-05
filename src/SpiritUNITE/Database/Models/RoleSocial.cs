using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_social")]
public sealed class RoleSocial
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("deny_all_request_flag")]
    public int DenyAllRequestFlag { get; set; }

    [Column("accept_chat_convention")]
    public int AcceptChatConvention { get; set; }

    [Column("last_logout_time")]
    public long LastLogoutTime { get; set; }
}
