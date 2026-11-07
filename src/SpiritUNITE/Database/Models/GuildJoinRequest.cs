using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("guild_join_request")]
public sealed class GuildJoinRequest
{
    [PrimaryKey(0)]
    [Column("guild_id")]
    public ulong GuildID { get; set; }

    [PrimaryKey(1)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("request_time")]
    public long RequestTime { get; set; }
}
