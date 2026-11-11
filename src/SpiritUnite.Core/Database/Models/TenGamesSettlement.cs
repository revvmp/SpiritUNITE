using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("battle_ten_games_settlement")]
public sealed class TenGamesSettlement
{
    [PrimaryKey, Column("settlement_id"), NotNull]
    public string SettlementID { get; set; } = string.Empty;

    [Column("plan"), NotNull]
    public byte[] Plan { get; set; } = [];

    [Column("applied")]
    public bool Applied { get; set; }

    [Column("created_at")]
    public long CreatedAt { get; set; }
}
