using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Profile;

public sealed class HeadFrameHandlers(RoleInfoStore roles, ProfileManager profiles, ILogger<HeadFrameHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetHeadIconInfoReq)]
    public async Task<GamePacket[]> OnGetHeadIconInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetHeadIconInfoReq.Parser.ParseFrom(request.PkgData);
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var response = new pbSCGetHeadIconInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            HeadIconInfo = profiles.BuildHeadIconInfo(role, style),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetHeadIconInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChangeCurHeadIconReq)]
    public async Task<GamePacket[]> OnChangeCurHeadIconReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var headIconID = pbCSChangeCurHeadIconReq.Parser.ParseFrom(request.PkgData).HeadIconID;
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = profiles.ChangeCurHeadIcon(role, style, headIconID);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await profiles.SaveAsync(role.GameUlongUID, style);
        }

        logger.LogInformation("Head frame {HeadIconID} for uid {Uid}: {Result}", headIconID, role.GameUlongUID, result);
        var response = new pbSCChangeCurHeadIconNtf
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            HeadIconID = style.CurHeadIconID,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChangeCurHeadIconNtf, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPersonStyleHeadInfoReq)]
    public async Task<GamePacket[]> OnPersonStyleHeadInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var queryUid = pbCSPersonStyleHeadInfoReq.Parser.ParseFrom(request.PkgData).Uid;
        var queried = queryUid == 0 ? null : await roles.FindAsync(queryUid);
        if (queried is null)
        {
            logger.LogInformation("Head portraits of unknown uid {QueryUid} requested by uid {Uid}", queryUid, request.Uid);
            var refusal = new pbSCPersonStyleHeadInfoRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid },
            };
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPersonStyleHeadInfoRsp, EmptyChildren.Fill(refusal))];
        }

        var style = await profiles.LoadAsync(queried.GameUlongUID);
        var response = new pbSCPersonStyleHeadInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            HeadInfo = ProfileManager.BuildPersonStyleHeadInfo(queried, style),
            HeadType = style.CurPersonStyleHead.HeadType,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPersonStyleHeadInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChangeCurPersonStyleHeadFrameReq)]
    public async Task<GamePacket[]> OnChangeCurPersonStyleHeadFrameReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var change = pbCSChangeCurPersonStyleHeadFrameReq.Parser.ParseFrom(request.PkgData);
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = profiles.ChangeCurPersonStyleHeadFrame(role, style, change.HeadFrameID, change.HeadType);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await profiles.SaveAsync(role.GameUlongUID, style);
        }

        logger.LogInformation(
            "Head portrait {HeadFrameID} type {HeadType} for uid {Uid}: {Result}",
            change.HeadFrameID,
            change.HeadType,
            role.GameUlongUID,
            result);
        var (headType, curHeadResID) = style.CurPersonStyleHead;
        var response = new pbSCChangeCurPersonStyleHeadFrameNtf
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            HeadFrameID = curHeadResID,
            HeadType = headType,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChangeCurPersonStyleHeadFrameNtf, response)];
    }
}
