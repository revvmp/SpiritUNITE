using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("battle_settlement_owner")]
public sealed class SettlementOwnerRecord
{
    [PrimaryKey, Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("fence")]
    public long Fence { get; set; }
}
