using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_all_pkm_challenge")]
public sealed class RoleAllPkmChallenge
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [PrimaryKey(2)]
    [Column("pkm_id")]
    public int PkmID { get; set; }

    [Column("is_played")]
    public int IsPlayed { get; set; }

    [Column("win_time")]
    public long WinTime { get; set; }
}
