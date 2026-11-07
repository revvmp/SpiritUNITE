using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_tmp_name")]
public sealed class RoleTmpName
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("tmp_name_type")]
    public int TmpNameType { get; set; }

    [Column("tmp_name")]
    public string TmpName { get; set; } = string.Empty;

    [Column("is_use")]
    public int IsUse { get; set; }

    [Column("is_valid")]
    public int IsValid { get; set; }
}
