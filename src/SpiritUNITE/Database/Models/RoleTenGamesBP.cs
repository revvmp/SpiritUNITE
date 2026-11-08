using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_ten_games_bp")]
public sealed class RoleTenGamesBP
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("activity_id")]
    public int ActivityID { get; set; }

    [Column("is_buy_senier_reward")]
    public int IsBuySenierReward { get; set; }

    [Column("win_cnt")]
    public int WinCnt { get; set; }
}
