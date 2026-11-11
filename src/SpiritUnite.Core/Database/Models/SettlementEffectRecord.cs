using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("battle_settlement_effect")]
public sealed class SettlementEffectRecord
{
    [PrimaryKey(0), Column("settlement_id"), NotNull]
    public string SettlementID { get; set; } = string.Empty;

    [PrimaryKey(1), Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(2), Column("effect"), NotNull]
    public string Effect { get; set; } = string.Empty;

    [Column("output"), NotNull]
    public byte[] Output { get; set; } = [];
}
