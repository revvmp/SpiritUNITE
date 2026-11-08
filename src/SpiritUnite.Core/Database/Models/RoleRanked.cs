using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_ranked")]
public sealed class RoleRanked
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("protect_card_select_status")]
    public int ProtectCardSelectStatus { get; set; }

    [Column("ban_rank_race_end_time")]
    public long BanRankRaceEndTime { get; set; }

    [Column("no_reminded_buy_protect_card_day")]
    public long NoRemindedBuyProtectCardDay { get; set; }
}
