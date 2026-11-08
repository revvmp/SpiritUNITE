using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_shop_buy")]
public sealed class RoleShopBuy
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("shop_id")]
    public int ShopID { get; set; }

    [Column("spec_sale_num")]
    public int SpecSaleNum { get; set; }

    [Column("spec_sale_cycle_time")]
    public long SpecSaleCycleTime { get; set; }
}
