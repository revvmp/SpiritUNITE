using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("server_list")]
public sealed class ServerList
{
    [PrimaryKey(0)]
    [Column("list_name")]
    public string ListName { get; set; } = string.Empty;

    [PrimaryKey(1)]
    [Column("list_index")]
    public ushort ListIndex { get; set; }

    [Column("item_id")]
    public int ItemId { get; set; }
}
