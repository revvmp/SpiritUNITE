using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_pve_challenge_node")]
public sealed class RolePveChallengeNode
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("stage_id")]
    public int StageID { get; set; }

    [PrimaryKey(2)]
    [Column("node_id")]
    public int NodeID { get; set; }

    [PrimaryKey(3)]
    [Column("difficulty_level")]
    public int DifficultyLevel { get; set; }

    [Column("star_num")]
    public int StarNum { get; set; }

    [Column("complete_time")]
    public long CompleteTime { get; set; }
}
