using System.Collections.Concurrent;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Settings;

public sealed class SettingsManager(SettingsRepository repository, GameData gameData)
{
    private const int QuickSpeakSignalType = (int)PB_ENUM_BATTLE_SIGNAL_COMMUNICATE_SIGNAL_TYPE.PbBattleSignalCommunicateSignalTypeShortcutphrase;
    private const int NoPhraseLocation = 0;
    private const int MaxGameSettingValues = 256;
    private const int MaxClientBit =
        (int)PB_MACRO_COM_MAX_NEWBIE_STATUS_BITS_NUM.PbComMaxNewbieStatusBitsNum * BitsOperationUtil.BitsPerWord - 1;
    private const int MaxRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdMax - 1;
    private const int MaxQuickSpeakNum = (int)PB_MACRO_MAX_QUICK_SPEAK_NUM.PbMaxQuickSpeakNum;
    private const int MaxRedDotCnt = (int)PB_MACRO_COM_MAX_RED_DOT_CNT.PbComMaxRedDotCnt;
    public const int RedDotSwitchedOn = 0;
    private const int RedDotSwitchedOff = 1;
    private const int SwitchOn = 1;
    private const int NoFriendInviteInvisibleType = (int)PB_ENUM_ENM_INVISIBLE_TYPE.PbEnmInvisibleTypeNofriendInvite;
    private const int ForeverInvisibleType = (int)PB_ENUM_ENM_INVISIBLE_TYPE.PbEnmInvisibleTypeForever;

    private readonly ConcurrentDictionary<ulong, RoleSettingsEntry> entries = new();

    private int[] DefaultQuickSpeakIds { get; } =
    [
        .. gameData.BattleSignalCommunicate.Rows
            .Where(row => row.SignalType == QuickSpeakSignalType && row.PhraseLocation != NoPhraseLocation)
            .Select(row => row.Id),
    ];

    public Task<RoleSettings> LoadAsync(ulong gameUlongUID) =>
        WithSettingsAsync(gameUlongUID, settings => (false, settings.Clone()));

    public Task<bool> HidesFriendshipAsync(ulong gameUlongUID) =>
        WithSettingsAsync(gameUlongUID, settings => (false, settings.IsCloseIntimacyShow == SwitchOn));

    public void Forget(ulong gameUlongUID) => entries.TryRemove(gameUlongUID, out _);

    public pbCOMDT_QUICK_SPEAK_INFO BuildQuickSpeakInfo(RoleSettings settings) =>
        settings.QuickSpeakInfo?.Clone() ?? new pbCOMDT_QUICK_SPEAK_INFO { QuickSpeakId = { DefaultQuickSpeakIds } };

    public static pbSCClientGameSettingNtf? BuildClientGameSettingNtf(RoleSettings settings) =>
        settings.GameSettingInfo.GameSettingValue.Count > 0
            ? new pbSCClientGameSettingNtf { GameSettingInfo = settings.GameSettingInfo }
            : null;

    public static pbSCRedDotInfoNtf BuildRedDotInfoNtf(RoleSettings settings) => new()
    {
        RedDotInfo = new pbCOMDT_RED_DOT_INFO
        {
            RedDotDetail = { settings.RedDotInfo.RedDotDetail },
            RedDotOnceInfo = new pbCOMDT_RED_DOT_ONCE_INFO(),
            RedDotSwitchInfo = settings.RedDotSwitchInfo,
        },
    };

    public static pbSCBubTipInfoNtf BuildBubTipInfoNtf(RoleSettings settings) => new() { BubTipInfo = settings.BubTipInfo };

    public async Task<PB_ENUM_RES_ERROR_CODE> SetClientBitAsync(ulong gameUlongUID, int clientBit)
    {
        if (clientBit is < 0 or > MaxClientBit)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await ChangeAsync(gameUlongUID, settings => BitsOperationUtil.SetBit(settings.ClientBitState.BitsDetail, clientBit));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetRedDotSwitchAsync(ulong gameUlongUID, int redDotID, int redDotSwitch)
    {
        if (redDotID is < 1 or > MaxRedDotID || redDotSwitch is not (RedDotSwitchedOn or RedDotSwitchedOff))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await ChangeAsync(gameUlongUID, settings => redDotSwitch == RedDotSwitchedOff
            ? BitsOperationUtil.SetBit(settings.RedDotSwitchInfo.RedDotSwitchBitMap, redDotID)
            : BitsOperationUtil.ReserveBit(settings.RedDotSwitchInfo.RedDotSwitchBitMap, redDotID)
                | BitsOperationUtil.ClearBit(settings.RedDotSwitchInfo.RedDotSwitchBitMap, redDotID));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public Task ReplaceRedDotsAsync(ulong gameUlongUID, IReadOnlyList<pbCOMDT_RED_DOT_DETAIL> redDotDetails) =>
        ChangeAsync(gameUlongUID, settings => redDotDetails.Aggregate(false, (changed, redDotDetail) =>
            ReplaceRedDot(settings.RedDotInfo.RedDotDetail, redDotDetail) | changed));

    public Task ClearRedDotAsync(ulong gameUlongUID, int redDotID) =>
        ChangeAsync(gameUlongUID, settings =>
            SwapRemove(settings.RedDotInfo.RedDotDetail, redDotDetail => redDotDetail.RedDotID == redDotID));

    public Task ReplaceBubTipsAsync(ulong gameUlongUID, IReadOnlyList<pbCOMDT_BUB_TIP_DETAIL> bubTipDetails) =>
        ChangeAsync(gameUlongUID, settings => bubTipDetails.Aggregate(false, (changed, bubTipDetail) =>
            ReplaceBubTip(settings.BubTipInfo.BubTipDetail, bubTipDetail) | changed));

    public Task ClearBubTipAsync(ulong gameUlongUID, int bubTipID) =>
        ChangeAsync(gameUlongUID, settings =>
            SwapRemove(settings.BubTipInfo.BubTipDetail, bubTipDetail => bubTipDetail.BubTipID == bubTipID));

    public async Task<PB_ENUM_RES_ERROR_CODE> SetLobbyMenuSortAsync(ulong gameUlongUID, IReadOnlyList<int> menuList)
    {
        if (menuList.Count > gameData.SystemMenuList.Rows.Count
            || menuList.Distinct().Count() != menuList.Count
            || !menuList.All(menuItem => gameData.SystemMenuList.Contains(menuItem)))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await ChangeAsync(gameUlongUID, settings =>
        {
            settings.MenuInfo = new pbCOMDT_LOBBY_MENU_SORT_INFO { MenuList = { menuList } };
            return true;
        });
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetABModeAsync(ulong gameUlongUID, int abMode)
    {
        if (abMode is not ((int)PB_ENUM_COM_ABMODE_TYPE.PbComAbmodeTypeA or (int)PB_ENUM_COM_ABMODE_TYPE.PbComAbmodeTypeB))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await ChangeAsync(gameUlongUID, settings => Assign(settings.ABMode, abMode, value => settings.ABMode = value));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetCountryCodeAsync(ulong gameUlongUID, int countryCode)
    {
        if (!gameData.NationSelectInformation.Contains(countryCode))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await ChangeAsync(gameUlongUID, settings => Assign(settings.CountryCode, countryCode, value => settings.CountryCode = value));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task AdoptLoginCountryCodeAsync(ulong gameUlongUID, int countryCode)
    {
        if (gameData.NationSelectInformation.Contains(countryCode))
        {
            await ChangeAsync(gameUlongUID, settings => settings.CountryCode == RoleSettings.NoCountryCode
                && Assign(settings.CountryCode, countryCode, value => settings.CountryCode = value));
        }
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetQuickSpeakAsync(ulong gameUlongUID, IReadOnlyList<int> quickSpeakIds)
    {
        if (quickSpeakIds.Count > MaxQuickSpeakNum
            || quickSpeakIds.Distinct().Count() != quickSpeakIds.Count
            || !quickSpeakIds.All(IsQuickSpeakPhrase))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await ChangeAsync(gameUlongUID, settings =>
        {
            settings.QuickSpeakInfo = new pbCOMDT_QUICK_SPEAK_INFO { QuickSpeakId = { quickSpeakIds } };
            return true;
        });
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetGameSettingAsync(ulong gameUlongUID, IReadOnlyList<int> gameSettingValues)
    {
        if (gameSettingValues.Count > MaxGameSettingValues)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await ChangeAsync(gameUlongUID, settings =>
        {
            settings.GameSettingInfo = new pbCOMDT_CLT_GAME_SETTING_INFO { GameSettingValue = { gameSettingValues } };
            return true;
        });
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetIntimacyShowAsync(ulong gameUlongUID, int isIntimacyShow)
    {
        if (!IsSwitch(isIntimacyShow))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        await ChangeAsync(gameUlongUID, settings =>
            Assign(settings.IsCloseIntimacyShow, isIntimacyShow, value => settings.IsCloseIntimacyShow = value));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> SetSelfInvisibleAsync(ulong gameUlongUID, int isInvisible, int invisibleType)
    {
        if (!IsSelfInvisibleChange(isInvisible, invisibleType))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var isForeverInvisible = invisibleType == ForeverInvisibleType ? SwitchOn : RoleSettings.Off;
        await ChangeAsync(gameUlongUID, settings => invisibleType == NoFriendInviteInvisibleType
            ? Assign(settings.IsNoFriendInviteInvisible, isInvisible, value => settings.IsNoFriendInviteInvisible = value)
            : Assign(settings.IsForeverInvisible, isForeverInvisible, value => settings.IsForeverInvisible = value));
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public static bool IsNoFriendInviteInvisible(int invisibleType) => invisibleType == NoFriendInviteInvisibleType;

    private static bool IsSwitch(int value) => value is RoleSettings.Off or SwitchOn;

    private static bool IsSelfInvisibleChange(int isInvisible, int invisibleType) => invisibleType switch
    {
        (int)PB_ENUM_ENM_INVISIBLE_TYPE.PbEnmInvisibleTypeTemp or (int)PB_ENUM_ENM_INVISIBLE_TYPE.PbEnmInvisibleTypeForever =>
            isInvisible == SwitchOn,
        (int)PB_ENUM_ENM_INVISIBLE_TYPE.PbEnmInvisibleTypeOnline => isInvisible == RoleSettings.Off,
        NoFriendInviteInvisibleType => IsSwitch(isInvisible),
        _ => false,
    };

    private static bool ReplaceRedDot(IList<pbCOMDT_RED_DOT_DETAIL> redDotDetails, pbCOMDT_RED_DOT_DETAIL redDotDetail)
    {
        if (redDotDetail.RedDotID is < 1 or > MaxRedDotID)
        {
            return false;
        }

        var index = IndexOf(redDotDetails, saved => saved.RedDotID == redDotDetail.RedDotID);
        if (index >= 0)
        {
            redDotDetails[index] = redDotDetail.Clone();
            return true;
        }

        if (redDotDetails.Count >= MaxRedDotCnt)
        {
            return false;
        }

        redDotDetails.Add(redDotDetail.Clone());
        return true;
    }

    private static bool ReplaceBubTip(IList<pbCOMDT_BUB_TIP_DETAIL> bubTipDetails, pbCOMDT_BUB_TIP_DETAIL bubTipDetail)
    {
        var index = IndexOf(bubTipDetails, saved => saved.BubTipID == bubTipDetail.BubTipID);
        if (index >= 0)
        {
            bubTipDetails[index] = bubTipDetail.Clone();
            return true;
        }

        bubTipDetails.Add(bubTipDetail.Clone());
        return true;
    }

    private static bool SwapRemove<T>(IList<T> details, Predicate<T> match)
    {
        var index = IndexOf(details, match);
        if (index < 0)
        {
            return false;
        }

        var last = details.Count - 1;
        details[index] = details[last];
        details.RemoveAt(last);
        return true;
    }

    private static int IndexOf<T>(IList<T> details, Predicate<T> match)
    {
        for (var index = 0; index < details.Count; index++)
        {
            if (match(details[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private bool IsQuickSpeakPhrase(int quickSpeakId) =>
        gameData.BattleSignalCommunicate.Find(quickSpeakId)?.SignalType == QuickSpeakSignalType;

    private static bool Assign(int current, int value, Action<int> assign)
    {
        if (current == value)
        {
            return false;
        }

        assign(value);
        return true;
    }

    private Task ChangeAsync(ulong gameUlongUID, Func<RoleSettings, bool> change) =>
        WithSettingsAsync(gameUlongUID, settings => (change(settings), true));

    private async Task<T> WithSettingsAsync<T>(ulong gameUlongUID, Func<RoleSettings, (bool Changed, T Result)> use)
    {
        var entry = await LoadEntryAsync(gameUlongUID);
        await entry.Gate.WaitAsync();
        try
        {
            var (changed, result) = use(entry.Settings);
            if (changed)
            {
                await repository.SaveAsync(gameUlongUID, entry.Settings);
            }

            return result;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    private async Task<RoleSettingsEntry> LoadEntryAsync(ulong gameUlongUID)
    {
        if (entries.TryGetValue(gameUlongUID, out var cached))
        {
            return cached;
        }

        var settings = await repository.FindAsync(gameUlongUID) ?? RoleSettings.CreateDefault();
        return entries.GetOrAdd(gameUlongUID, new RoleSettingsEntry(settings));
    }

    private sealed class RoleSettingsEntry(RoleSettings settings)
    {
        public RoleSettings Settings { get; } = settings;
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
