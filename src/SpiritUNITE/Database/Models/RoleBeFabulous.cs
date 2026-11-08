using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_be_fabulous")]
public sealed class RoleBeFabulous
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("be_fabulous_cnt")]
    public int BeFabulousCnt { get; set; }

    [Column("be_fbls_cnt_last_upd_time")]
    public long BeFblsCntLastUpdTime { get; set; }
}
