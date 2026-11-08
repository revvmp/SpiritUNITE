using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("guild_activity")]
public sealed class GuildActivity
{
    [PrimaryKey(0)]
    [Column("guild_id")]
    public ulong GuildID { get; set; }

    [PrimaryKey(1)]
    [Column("period_id")]
    public int PeriodID { get; set; }

    [Column("reward_id")]
    public int RewardID { get; set; }

    [Column("guild_score")]
    public int GuildScore { get; set; }
}
