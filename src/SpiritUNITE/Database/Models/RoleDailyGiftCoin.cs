using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_daily_gift_coin")]
public sealed class RoleDailyGiftCoin
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("gift_day")]
    public long GiftDay { get; set; }

    [PrimaryKey(2)]
    [Column("daily_gift_coin_uid")]
    public ulong DailyGiftCoinUid { get; set; }

    [Column("gift_time")]
    public long GiftTime { get; set; }
}
