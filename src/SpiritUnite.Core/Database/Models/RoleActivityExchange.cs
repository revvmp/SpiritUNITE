using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_activity_exchange")]
public sealed class RoleActivityExchange
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("exchange_id")]
    public int ExchangeID { get; set; }

    [Column("done_count")]
    public int DoneCount { get; set; }

    [Column("notify")]
    public int Notify { get; set; }
}
