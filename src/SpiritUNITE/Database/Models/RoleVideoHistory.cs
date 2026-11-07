using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_video_history")]
public sealed class RoleVideoHistory
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("abs_id")]
    public string AbsId { get; set; } = string.Empty;

    [Column("save_time")]
    public long SaveTime { get; set; }

    [Column("collect_flag")]
    public int CollectFlag { get; set; }

    [Column("battle_version_section1")]
    public ulong BattleVersionSection1 { get; set; }

    [Column("battle_version_section2")]
    public ulong BattleVersionSection2 { get; set; }

    [Column("fight_history_record")]
    public byte[] FightHistoryRecord { get; set; } = [];
}
