using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_accusation")]
public sealed class RoleAccusation
{
    [PrimaryKey]
    [Identity]
    [Column("accusation_id")]
    public ulong AccusationId { get; set; }

    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("accused_uid")]
    public ulong AccusedUid { get; set; }

    [Column("accused_account_id")]
    public string AccusedAccountID { get; set; } = "";

    [Column("accused_role_name")]
    public string AccusedRoleName { get; set; } = "";

    [Column("category")]
    public int Category { get; set; }

    [Column("scene")]
    public int Scene { get; set; }

    [Column("reason")]
    public string Reason { get; set; } = "";

    [Column("battle_id")]
    public string BattleID { get; set; } = "";

    [Column("battle_time")]
    public int BattleTime { get; set; }

    [Column("relay_id")]
    public string RelayID { get; set; } = "";

    [Column("description")]
    public string Desc { get; set; } = "";

    [Column("content")]
    public string Content { get; set; } = "";

    [Column("voice_accusation_source")]
    public int VoiceAccusationSource { get; set; }

    [Column("accusation_result")]
    public int AccusationResult { get; set; }

    [Column("credit_punish")]
    public int CreditPunish { get; set; }

    [Column("accusation_time")]
    public long AccusationTime { get; set; }
}
