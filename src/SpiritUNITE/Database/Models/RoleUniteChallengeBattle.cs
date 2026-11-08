using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_unite_challenge_battle")]
public sealed class RoleUniteChallengeBattle
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("period_id")]
    public int PeriodId { get; set; }

    [PrimaryKey(2)]
    [Column("battle_index")]
    public int BattleIndex { get; set; }

    [Column("battle_status")]
    public int BattleStatus { get; set; }
}
