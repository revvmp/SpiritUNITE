using LinqToDB.Mapping;

namespace SpiritUnite.Database.Models;

[Table("role_profile_style")]
public sealed class RoleProfileStyle
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("cur_head_icon_id")]
    public int CurHeadIconID { get; set; }

    [Column("player_contestant_info")]
    public byte[] PlayerContestantInfo { get; set; } = [];

    [Column("individual_label_info")]
    public byte[] IndividualLabelInfo { get; set; } = [];

    [Column("is_anonymity")]
    public int IsAnonymity { get; set; }

    [Column("is_hide_info")]
    public int IsHideInfo { get; set; }

    [Column("cur_canopy_id")]
    public int CurCanopyID { get; set; }

    [Column("head_type")]
    public int HeadType { get; set; }

    [Column("cur_head_res_id")]
    public int CurHeadResID { get; set; }
}
