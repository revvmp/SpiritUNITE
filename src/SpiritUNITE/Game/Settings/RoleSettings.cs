using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Settings;

public sealed class RoleSettings(
    pbCOMDT_NEWBIE_STATE_INFO clientBitState,
    pbCOMDT_RED_DOT_SWITCH_INFO redDotSwitchInfo,
    pbCOMDT_LOBBY_MENU_SORT_INFO menuInfo,
    pbCOMDT_QUICK_SPEAK_INFO? quickSpeakInfo,
    pbCOMDT_CLT_GAME_SETTING_INFO gameSettingInfo,
    int abMode,
    int countryCode,
    pbCOMDT_RED_DOT_INFO redDotInfo,
    pbCOMDT_BUB_TIP_INFO bubTipInfo,
    int isCloseIntimacyShow,
    int isForeverInvisible,
    int isNoFriendInviteInvisible)
{
    public const int NoCountryCode = 0;
    public const int Off = 0;
    public const int On = 1;

    public pbCOMDT_NEWBIE_STATE_INFO ClientBitState { get; } = clientBitState;
    public pbCOMDT_RED_DOT_SWITCH_INFO RedDotSwitchInfo { get; } = redDotSwitchInfo;
    public pbCOMDT_LOBBY_MENU_SORT_INFO MenuInfo { get; set; } = menuInfo;
    public pbCOMDT_QUICK_SPEAK_INFO? QuickSpeakInfo { get; set; } = quickSpeakInfo;
    public pbCOMDT_CLT_GAME_SETTING_INFO GameSettingInfo { get; set; } = gameSettingInfo;
    public int ABMode { get; set; } = abMode;
    public int CountryCode { get; set; } = countryCode;
    public pbCOMDT_RED_DOT_INFO RedDotInfo { get; } = redDotInfo;
    public pbCOMDT_BUB_TIP_INFO BubTipInfo { get; } = bubTipInfo;
    public int IsCloseIntimacyShow { get; set; } = isCloseIntimacyShow;
    public int IsForeverInvisible { get; set; } = isForeverInvisible;
    public int IsNoFriendInviteInvisible { get; set; } = isNoFriendInviteInvisible;

    public RoleSettings Clone() => new(
        ClientBitState.Clone(),
        RedDotSwitchInfo.Clone(),
        MenuInfo.Clone(),
        QuickSpeakInfo?.Clone(),
        GameSettingInfo.Clone(),
        ABMode,
        CountryCode,
        RedDotInfo.Clone(),
        BubTipInfo.Clone(),
        IsCloseIntimacyShow,
        IsForeverInvisible,
        IsNoFriendInviteInvisible);

    public static RoleSettings CreateDefault() => new(
        new pbCOMDT_NEWBIE_STATE_INFO(),
        new pbCOMDT_RED_DOT_SWITCH_INFO(),
        new pbCOMDT_LOBBY_MENU_SORT_INFO(),
        null,
        new pbCOMDT_CLT_GAME_SETTING_INFO(),
        (int)PB_ENUM_COM_ABMODE_TYPE.PbComAbmodeTypeA,
        NoCountryCode,
        new pbCOMDT_RED_DOT_INFO(),
        new pbCOMDT_BUB_TIP_INFO(),
        Off,
        Off,
        Off);
}
