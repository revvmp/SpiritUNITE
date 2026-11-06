using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_newbie_challenge")]
public sealed class RoleNewbieChallenge
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("recv_time")]
    public long RecvTime { get; set; }
}
