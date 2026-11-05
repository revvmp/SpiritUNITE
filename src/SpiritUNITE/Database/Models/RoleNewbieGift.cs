using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_newbie_gift")]
public sealed class RoleNewbieGift
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("gift_id")]
    public int GiftID { get; set; }

    [Column("fetch_time")]
    public long FetchTime { get; set; }
}
