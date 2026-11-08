using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_tower_defense_shop")]
public sealed class RoleTowerDefenseShop
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("period_id")]
    public int PeriodID { get; set; }

    [PrimaryKey(2)]
    [Column("shop_id")]
    public int ShopID { get; set; }

    [Column("shop_buy_num")]
    public int ShopBuyNum { get; set; }
}
