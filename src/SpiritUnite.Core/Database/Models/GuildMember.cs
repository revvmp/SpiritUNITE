using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("guild_member")]
public sealed class GuildMember
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("guild_id")]
    public ulong GuildID { get; set; }

    [Column("office")]
    public int Office { get; set; }

    [Column("join_time")]
    public long JoinTime { get; set; }
}
