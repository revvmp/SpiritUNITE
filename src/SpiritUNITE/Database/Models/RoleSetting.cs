using LinqToDB.Mapping;

namespace SpiritUNITE.Database.Models;

[Table("role_setting")]
public sealed class RoleSetting
{
    [PrimaryKey]
    [Column("game_ulong_uid")]
    public ulong GameUlongUID { get; set; }

    [Column("client_bit_state")]
    public byte[] ClientBitState { get; set; } = [];

    [Column("red_dot_switch_info")]
    public byte[] RedDotSwitchInfo { get; set; } = [];

    [Column("lobby_menu_sort_info")]
    public byte[] LobbyMenuSortInfo { get; set; } = [];

    [Column("quick_speak_info")]
    public byte[]? QuickSpeakInfo { get; set; }

    [Column("game_setting_info")]
    public byte[] GameSettingInfo { get; set; } = [];

    [Column("ab_mode")]
    public int ABMode { get; set; }

    [Column("country_code")]
    public int CountryCode { get; set; }

    [Column("red_dot_info")]
    public byte[] RedDotInfo { get; set; } = [];

    [Column("bub_tip_info")]
    public byte[] BubTipInfo { get; set; } = [];

    [Column("is_close_intimacy_show")]
    public int IsCloseIntimacyShow { get; set; }

    [Column("is_invisible")]
    public int IsInvisible { get; set; }

    [Column("is_no_friend_invite_invisible")]
    public int IsNoFriendInviteInvisible { get; set; }
}
