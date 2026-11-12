using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_used_personalise_button")]
public sealed class RoleUsedPersonaliseButton
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("paster_id")]
    public int PasterId { get; set; }

    [Column("button_id")]
    public int ButtonID { get; set; }
}
