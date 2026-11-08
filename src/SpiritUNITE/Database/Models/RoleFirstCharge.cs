using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_first_charge")]
public sealed class RoleFirstCharge
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("first_charge_time")]
    public long FirstChargeTime { get; set; }

    [Column("last_face_shot_time")]
    public long LastFaceShotTime { get; set; }

    [Column("face_shot_cnt")]
    public int FaceShotCnt { get; set; }

    [Column("is_ban_face_shot")]
    public int IsBanFaceShot { get; set; }
}
