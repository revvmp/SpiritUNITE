using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_fight_history")]
public sealed class RoleFightHistory
{
    [PrimaryKey]
    [Identity]
    [Column("fight_history_id")]
    public ulong FightHistoryId { get; set; }

    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("map_sub_mode")]
    public int MapSubMode { get; set; }

    [Column("fight_history_record")]
    public byte[] FightHistoryRecord { get; set; } = [];

    [Column("battle_review_info")]
    public byte[] BattleReviewInfo { get; set; } = [];

    [Column("record_time")]
    public long RecordTime { get; set; }
}
