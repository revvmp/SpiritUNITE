using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("battle_settlement")]
public sealed class BattleSettlementRecord
{
    [PrimaryKey(0), Column("settlement_id"), NotNull]
    public string SettlementID { get; set; } = string.Empty;

    [PrimaryKey(1), Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("state")]
    public int State { get; set; }

    [Column("payload"), NotNull]
    public byte[] Payload { get; set; } = [];

    [Column("updated_at")]
    public long UpdatedAt { get; set; }

    [Column("last_error"), NotNull]
    public string LastError { get; set; } = string.Empty;
}
