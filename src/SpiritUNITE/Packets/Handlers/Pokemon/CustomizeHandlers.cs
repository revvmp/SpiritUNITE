using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Pokemon;

public sealed class CustomizeHandlers(ILogger<CustomizeHandlers> logger)
{
    private const int DEFAULT_PERSONALISE_BUTTON_ID = 0;
    private const int DefaultGloryAnnounceID = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetPersonaliseButtonReq)]
    public GamePacket[] OnGetPersonaliseButtonReq(GamePacket request)
    {
        pbCSGetPersonaliseButtonReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetPersonaliseButtonRsp
        {
            ResultInfo = new pbResultInfo(),
            PersonaliseButtonInfo = new pbCOMDT_PERSONALISE_BUTTON_LIST(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetPersonaliseButtonRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetPersonaliseButtonReq)]
    public GamePacket[] OnSetPersonaliseButtonReq(GamePacket request)
    {
        var selection = pbCSSetPersonaliseButtonReq.Parser.ParseFrom(request.PkgData);
        var result = selection.ButtonID == DEFAULT_PERSONALISE_BUTTON_ID
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrPersonaliseButtonNotValid;
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Personalise button {ButtonID} refused for uid {Uid}: {Result}", selection.ButtonID, request.Uid, result);
        }

        var response = new pbSCSetPersonaliseButtonRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            PasterID = selection.PasterID,
            ButtonID = selection.ButtonID,
            PasterList = selection.PasterList ?? new pbCOMDT_PASTERID_LIST(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetPersonaliseButtonRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetGloryAnnounceListReq)]
    public GamePacket[] OnGetGloryAnnounceListReq(GamePacket request)
    {
        pbCSGetGloryAnnounceListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetGloryAnnounceListRsp { ResultInfo = new pbResultInfo() };
        return
        [
            request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf,
                LobbyNotices.BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewGloryAnnounce)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetGloryAnnounceListRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetGloryAnnounceReq)]
    public GamePacket[] OnSetGloryAnnounceReq(GamePacket request)
    {
        var announce = pbCSSetGloryAnnounceReq.Parser.ParseFrom(request.PkgData).GloryAnnounceInfo
            ?? new pbCSDT_GLORY_ANNOUNCE_INFO();
        var result = announce.AnnounceID == DefaultGloryAnnounceID
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Glory announce {AnnounceID} refused for uid {Uid}: {Result}", announce.AnnounceID, request.Uid, result);
        }

        var response = new pbSCSetGloryAnnounceRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            GloryAnnounceInfo = announce,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetGloryAnnounceRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGloryAnnounceClearNewTagReq)]
    public GamePacket[] OnGloryAnnounceClearNewTagReq(GamePacket request)
    {
        var announceID = pbCSGloryAnnounceClearNewTagReq.Parser.ParseFrom(request.PkgData).AnnounceID;
        const PB_ENUM_RES_ERROR_CODE result = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        logger.LogInformation("Glory announce {AnnounceID} new tag clear refused for uid {Uid}: {Result}", announceID, request.Uid, result);
        var response = new pbSCGloryAnnounceClearNewTagRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            GloryAnnounceInfo = new pbCSDT_GLORY_ANNOUNCE_INFO { AnnounceID = announceID },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGloryAnnounceClearNewTagRsp, response)];
    }
}
