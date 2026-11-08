using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_challenge_trainer_record")]
public sealed class RoleChallengeTrainerRecord
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("map_id")]
    public int MapId { get; set; }

    [PrimaryKey(2)]
    [Column("level_type")]
    public int LevelType { get; set; }

    [Column("challenge_cnt")]
    public int ChallengeCnt { get; set; }
}
