using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("gift_code_award")]
public sealed class GiftCodeAward
{
    [PrimaryKey(0)]
    [Column("gift_code")]
    public string GiftCode { get; set; } = string.Empty;

    [PrimaryKey(1)]
    [Column("award_index")]
    public byte AwardIndex { get; set; }

    [Column("res_type")]
    public int ResType { get; set; }

    [Column("res_id")]
    public int ResID { get; set; }

    [Column("res_cnt")]
    public int ResCnt { get; set; }
}
