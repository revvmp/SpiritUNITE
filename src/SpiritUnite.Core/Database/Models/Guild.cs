using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("guild")]
public sealed class Guild
{
    [PrimaryKey]
    [Identity]
    [Column("guild_id")]
    public ulong GuildID { get; set; }

    [Column("guild_name")]
    public string GuildName { get; set; } = "";

    [Column("notice")]
    public string Notice { get; set; } = "";

    [Column("join_type")]
    public int JoinType { get; set; }

    [Column("label_1")]
    public int Label1 { get; set; }

    [Column("label_2")]
    public int Label2 { get; set; }

    [Column("label_3")]
    public int Label3 { get; set; }

    [Column("build_time")]
    public long BuildTime { get; set; }
}
