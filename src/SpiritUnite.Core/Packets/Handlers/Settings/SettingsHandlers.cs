using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Settings;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Settings;

public sealed class SettingsHandlers(
    RoleInfoStore roles,
    SettingsManager settings,
    FriendPresence presence,
    TimeProvider clock,
    ILogger<SettingsHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetClientBitStateReq)]
    public async Task<GamePacket[]> OnSetClientBitStateReq(GamePacket request)
    {
        var clientBit = pbCSSetClientBitStateReq.Parser.ParseFrom(request.PkgData).ClientBit;
        var role = roles.Require(request.Uid);
        var result = await settings.SetClientBitAsync(role.GameUlongUID, clientBit);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Client bit {ClientBit} refused for uid {Uid}: {Result}", clientBit, request.Uid, result);
            return [];
        }

        var saved = await settings.LoadAsync(role.GameUlongUID);
        var update = new pbSCClientBitStateNtf { ClientBitState = saved.ClientBitState };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClientBitStateNtf, update)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRedDotSwitchReq)]
    public async Task<GamePacket[]> OnRedDotSwitchReq(GamePacket request)
    {
        var redDotSwitch = pbCSRedDotSwitchReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var result = await settings.SetRedDotSwitchAsync(role.GameUlongUID, redDotSwitch.RedDotID, redDotSwitch.RedDotSwitch);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Red dot {RedDotID} switch {RedDotSwitch} refused for uid {Uid}: {Result}",
                redDotSwitch.RedDotID, redDotSwitch.RedDotSwitch, request.Uid, result);
        }

        var switchedOn = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc && redDotSwitch.RedDotSwitch == SettingsManager.RedDotSwitchedOn;
        return
        [
            .. switchedOn
                ? [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = redDotSwitch.RedDotID })]
                : Array.Empty<GamePacket>(),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRedDotSwitchRsp, new pbSCRedDotSwitchRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRedDotClearReq)]
    public GamePacket[] OnRedDotClearReq(GamePacket request)
    {
        var redDotID = pbCSRedDotClearReq.Parser.ParseFrom(request.PkgData).RedDotID;
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = redDotID })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBubTipClearReq)]
    public GamePacket[] OnBubTipClearReq(GamePacket request)
    {
        var bubTipID = pbCSBubTipClearReq.Parser.ParseFrom(request.PkgData).BubTipID;
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBubTipClearNtf, new pbSCBubTipClearNtf { BubTipID = bubTipID }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBubTipClearRsp, new pbSCBubTipClearRsp { ResultInfo = new pbResultInfo() }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClientGameSettingReq)]
    public async Task<GamePacket[]> OnClientGameSettingReq(GamePacket request)
    {
        var gameSettingInfo = pbCSClientGameSettingReq.Parser.ParseFrom(request.PkgData).GameSettingInfo
            ?? new pbCOMDT_CLT_GAME_SETTING_INFO();
        var role = roles.Require(request.Uid);
        var result = await settings.SetGameSettingAsync(role.GameUlongUID, gameSettingInfo.GameSettingValue);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Client game setting with {Count} values refused for uid {Uid}: {Result}",
                gameSettingInfo.GameSettingValue.Count, request.Uid, result);
        }

        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClientGameSettingRsp, new pbSCClientGameSettingRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetIntimacyShowReq)]
    public async Task<GamePacket[]> OnSetIntimacyShowReq(GamePacket request)
    {
        var isIntimacyShow = pbCSSetIntimacyShowReq.Parser.ParseFrom(request.PkgData).IsIntimacyShow;
        var role = roles.Require(request.Uid);
        var result = await settings.SetIntimacyShowAsync(role.GameUlongUID, isIntimacyShow);
        logger.LogInformation("Intimacy show {IsIntimacyShow} for uid {Uid}: {Result}", isIntimacyShow, request.Uid, result);
        var saved = await settings.LoadAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetIntimacyShowRsp, new pbSCSetIntimacyShowRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            IsIntimacyShow = saved.IsCloseIntimacyShow,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetSelfInvisibleReq)]
    public async Task<GamePacket[]> OnSetSelfInvisibleReq(GamePacket request)
    {
        var selfInvisible = pbCSSetSelfInvisibleReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var result = await settings.SetSelfInvisibleAsync(role.GameUlongUID, selfInvisible.IsInvisible, selfInvisible.InvisibleType);
        logger.LogInformation("Self invisible {IsInvisible} type {InvisibleType} for uid {Uid}: {Result}",
            selfInvisible.IsInvisible, selfInvisible.InvisibleType, request.Uid, result);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await presence.SetInvisibleAsync(
                role.GameUlongUID,
                (PB_ENUM_ENM_INVISIBLE_TYPE)selfInvisible.InvisibleType,
                clock.GetUtcNow().ToUnixTimeSeconds());
        }

        var saved = await settings.LoadAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetSelfInvisibleRsp, new pbSCSetSelfInvisibleRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            IsInvisible = SettingsManager.IsNoFriendInviteInvisible(selfInvisible.InvisibleType)
                ? saved.IsNoFriendInviteInvisible
                : presence.GetSelfInvisible(role.GameUlongUID),
            InvisibleType = selfInvisible.InvisibleType,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetLobbyMenuSortReq)]
    public async Task<GamePacket[]> OnSetLobbyMenuSortReq(GamePacket request)
    {
        var menuInfo = pbCSSetLobbyMenuSortReq.Parser.ParseFrom(request.PkgData).MenuInfo ?? new pbCOMDT_LOBBY_MENU_SORT_INFO();
        var role = roles.Require(request.Uid);
        var result = await settings.SetLobbyMenuSortAsync(role.GameUlongUID, menuInfo.MenuList);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Lobby menu sort [{MenuList}] refused for uid {Uid}: {Result}",
                string.Join(", ", menuInfo.MenuList), request.Uid, result);
        }

        var saved = await settings.LoadAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetLobbyMenuSortRsp, new pbSCSetLobbyMenuSortRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            MenuInfo = saved.MenuInfo,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAbmodeReq)]
    public async Task<GamePacket[]> OnGetABModeReq(GamePacket request)
    {
        pbCSGetABModeReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var saved = await settings.LoadAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAbmodeRsp, new pbSCGetABModeRsp
        {
            ResultInfo = new pbResultInfo(),
            ABMode = saved.ABMode,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetAbmodeReq)]
    public async Task<GamePacket[]> OnSetABModeReq(GamePacket request)
    {
        var abMode = pbCSSetABModeReq.Parser.ParseFrom(request.PkgData).ABMode;
        var role = roles.Require(request.Uid);
        var result = await settings.SetABModeAsync(role.GameUlongUID, abMode);
        logger.LogInformation("AB mode {ABMode} for uid {Uid}: {Result}", abMode, request.Uid, result);
        var saved = await settings.LoadAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetAbmodeRsp, new pbSCSetABModeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            ABMode = saved.ABMode,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetCountryCodeReq)]
    public async Task<GamePacket[]> OnSetCountryCodeReq(GamePacket request)
    {
        var countryCode = pbCSSetCountryCodeReq.Parser.ParseFrom(request.PkgData).CountryCode;
        var role = roles.Require(request.Uid);
        var result = await settings.SetCountryCodeAsync(role.GameUlongUID, countryCode);
        logger.LogInformation("Country code {CountryCode} for uid {Uid}: {Result}", countryCode, request.Uid, result);
        var saved = await settings.LoadAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetCountryCodeRsp, new pbSCSetCountryCodeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            CountryCode = saved.CountryCode,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetQuickSpeakCfgReq)]
    public async Task<GamePacket[]> OnGetQuickSpeakCfgReq(GamePacket request)
    {
        pbCSGetQuickSpeakCfgReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var saved = await settings.LoadAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetQuickSpeakCfgRsp, new pbSCGetQuickSpeakCfgRsp
        {
            ResultInfo = new pbResultInfo(),
            QuickSpeakInfo = settings.BuildQuickSpeakInfo(saved),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetQuickSpeakCfgReq)]
    public async Task<GamePacket[]> OnSetQuickSpeakCfgReq(GamePacket request)
    {
        var quickSpeakInfo = pbCSSetQuickSpeakCfgReq.Parser.ParseFrom(request.PkgData).QuickSpeakInfo
            ?? new pbCOMDT_QUICK_SPEAK_INFO();
        var role = roles.Require(request.Uid);
        var result = await settings.SetQuickSpeakAsync(role.GameUlongUID, quickSpeakInfo.QuickSpeakId);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetQuickSpeakCfgRsp, new pbSCSetQuickSpeakCfgRsp
            {
                ResultInfo = new pbResultInfo(),
            })];
        }

        logger.LogInformation("Quick chat [{QuickSpeakIds}] refused for uid {Uid}: {Result}",
            string.Join(", ", quickSpeakInfo.QuickSpeakId), request.Uid, result);
        var saved = await settings.LoadAsync(role.GameUlongUID);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetQuickSpeakCfgRsp, new pbSCSetQuickSpeakCfgRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)result },
            }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidQuickSpeakCfgNtf, new pbSCQuickSpeakCfgNtf
            {
                QuickSpeakInfo = settings.BuildQuickSpeakInfo(saved),
            }),
        ];
    }
}
