using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_warm_battle")]
public sealed class RoleWarmBattle
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("map_sub_mode")]
    public int MapSubMode { get; set; }

    [Column("game_cnt")]
    public int GameCnt { get; set; }

    [Column("single_warm_cnt")]
    public int SingleWarmCnt { get; set; }

    [Column("mult_warm_cnt")]
    public int MultWarmCnt { get; set; }

    [Column("cont_lose_cnt")]
    public int ContLoseCnt { get; set; }
}
