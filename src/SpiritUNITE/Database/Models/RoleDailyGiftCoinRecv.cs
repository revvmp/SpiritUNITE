using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_daily_gift_coin_recv")]
public sealed class RoleDailyGiftCoinRecv
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("gift_day")]
    public long GiftDay { get; set; }

    [Column("recv_count")]
    public int RecvCount { get; set; }
}
