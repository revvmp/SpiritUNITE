using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_coin")]
public sealed class RoleCoin
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("coin_id")]
    public int CoinID { get; set; }

    [Column("coin_val")]
    public long CoinVal { get; set; }

    [Column("coin_seq", SkipOnInsert = true, SkipOnUpdate = true)]
    public ulong CoinSeq { get; set; }
}
