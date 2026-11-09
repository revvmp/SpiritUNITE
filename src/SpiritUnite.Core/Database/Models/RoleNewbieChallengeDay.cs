using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_newbie_challenge_day")]
public sealed class RoleNewbieChallengeDay
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey]
    [Column("task_day")]
    public int TaskDay { get; set; }

    [Column("recv_time")]
    public long RecvTime { get; set; }
}
