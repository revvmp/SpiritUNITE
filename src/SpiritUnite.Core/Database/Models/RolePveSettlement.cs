using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_pve_settlement")]
public sealed class RolePveSettlement
{
    [PrimaryKey, Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("settlement_id"), NotNull]
    public string SettlementID { get; set; } = string.Empty;

    [Column("battle_started_at")]
    public long BattleStartedAt { get; set; }

    [Column("stage_id")]
    public int StageID { get; set; }

    [Column("node_id")]
    public int NodeID { get; set; }

    [Column("difficulty_level")]
    public int DifficultyLevel { get; set; }

    [Column("claim_state")]
    public int ClaimState { get; set; }

    [Column("refresh_drops"), NotNull]
    public string RefreshDrops { get; set; } = "[]";
}
