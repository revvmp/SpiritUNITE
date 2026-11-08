using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_held_item_slot")]
public sealed class RoleHeldItemSlot
{
    [PrimaryKey(0)]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [PrimaryKey(1)]
    [Column("held_item_slot_index")]
    public int HeldItemSlotIndex { get; set; }
}
