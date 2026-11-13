using Microsoft.Extensions.Logging;
using SpiritUnite.Battle;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Pokemon;

public sealed class CustomizeHandlers(RoleInfoStore roles, CustomizeManager customize, DeskManager desks, ILogger<CustomizeHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetPersonaliseButtonReq)]
    public async Task<GamePacket[]> OnGetPersonaliseButtonReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetPersonaliseButtonReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetPersonaliseButtonRsp
        {
            ResultInfo = new pbResultInfo(),
            PersonaliseButtonInfo = await customize.BuildPersonaliseButtonListAsync(role),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetPersonaliseButtonRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetPersonaliseButtonReq)]
    public async Task<GamePacket[]> OnSetPersonaliseButtonReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var selection = pbCSSetPersonaliseButtonReq.Parser.ParseFrom(request.PkgData);
        var outcome = await customize.SetPersonaliseButtonAsync(role, selection);
        if (outcome.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Personalise button {ButtonID} refused for uid {Uid}: {Result}", selection.ButtonID, request.Uid, outcome.Result);
        }
        else
        {
            logger.LogInformation("Personalise button {ButtonID} set on {Count} holowear for uid {Uid}", selection.ButtonID, outcome.PasterIds.Length, request.Uid);
        }

        var pickNotices = outcome.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? await desks.DeliverAsync(request.Uid, await desks.ChangeCosmeticsAsync(role, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoicePersonaliseButtonChg))
            : [];

        var response = new pbSCSetPersonaliseButtonRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            PasterID = selection.PasterID,
            ButtonID = selection.ButtonID,
            PasterList = selection.PasterList ?? new pbCOMDT_PASTERID_LIST(),
        };
        return
        [
            .. pickNotices.Select(message => request.Reply(message.MsgID, message.Message)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetPersonaliseButtonRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetGloryAnnounceListReq)]
    public async Task<GamePacket[]> OnGetGloryAnnounceListReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetGloryAnnounceListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetGloryAnnounceListRsp
        {
            ResultInfo = new pbResultInfo(),
            GloryAnnounceList = { await customize.BuildGloryAnnounceListAsync(role) },
        };
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf,
                LobbyNotices.BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewGloryAnnounce)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetGloryAnnounceListRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetGloryAnnounceReq)]
    public async Task<GamePacket[]> OnSetGloryAnnounceReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var announce = pbCSSetGloryAnnounceReq.Parser.ParseFrom(request.PkgData).GloryAnnounceInfo
            ?? new pbCSDT_GLORY_ANNOUNCE_INFO();
        var result = await customize.SetGloryAnnounceAsync(role, announce);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation(
                "Glory announce {AnnounceID} on Pokemon [{PokemonList}] and holowear [{PasterList}] refused for uid {Uid}: {Result}",
                announce.AnnounceID,
                string.Join(',', announce.PokemonList),
                string.Join(',', announce.PasterList),
                request.Uid,
                result);
        }
        else
        {
            logger.LogInformation(
                "Glory announce {AnnounceID} set on {PokemonCount} Pokemon and {PasterCount} holowear for uid {Uid}",
                announce.AnnounceID,
                announce.PokemonList.Count,
                announce.PasterList.Count,
                request.Uid);
        }

        var pickNotices = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? await desks.DeliverAsync(request.Uid, await desks.ChangeCosmeticsAsync(role, PB_ENUM_ENM_CHOICE_HERO_CHG_REASON.PbEnmChoiceGloryAnnounceChg))
            : [];

        var response = new pbSCSetGloryAnnounceRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            GloryAnnounceInfo = announce,
        };
        return
        [
            .. pickNotices.Select(message => request.Reply(message.MsgID, message.Message)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetGloryAnnounceRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGloryAnnounceClearNewTagReq)]
    public async Task<GamePacket[]> OnGloryAnnounceClearNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var announceID = pbCSGloryAnnounceClearNewTagReq.Parser.ParseFrom(request.PkgData).AnnounceID;
        var result = await customize.ClearGloryAnnounceNewTagAsync(role, announceID);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Glory announce {AnnounceID} new tag clear refused for uid {Uid}: {Result}", announceID, request.Uid, result);
        }

        var response = new pbSCGloryAnnounceClearNewTagRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            GloryAnnounceInfo = new pbCSDT_GLORY_ANNOUNCE_INFO { AnnounceID = announceID },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGloryAnnounceClearNewTagRsp, response)];
    }
}
