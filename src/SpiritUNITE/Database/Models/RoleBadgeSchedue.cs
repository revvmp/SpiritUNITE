using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_badge_schedue")]
public sealed class RoleBadgeSchedue
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("badge_schedue_id")]
    public int BadgeSchedueId { get; set; }

    [Column("schedue_name")]
    public string SchedueName { get; set; } = "";

    [Column("schedue_data")]
    public byte[] SchedueData { get; set; } = [];

    [Column("unlock_time_stamp")]
    public long UnlockTimeStamp { get; set; }
}
