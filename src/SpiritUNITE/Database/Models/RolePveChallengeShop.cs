using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_pve_challenge_shop")]
public sealed class RolePveChallengeShop
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("shop_id")]
    public int ShopID { get; set; }

    [Column("shop_buy_num")]
    public int ShopBuyNum { get; set; }
}
