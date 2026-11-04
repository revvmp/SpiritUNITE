using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Profile;

public sealed class EntrycardHandlers(RoleInfoStore roles, ProfileManager profiles, ILogger<EntrycardHandlers> logger)
{
    private const int MaximumContextLength = 64;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAcntProfileNewReq)]
    public async Task<GamePacket[]> OnGetAcntProfileNewReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSGetAcntProfileNewReq.Parser.ParseFrom(request.PkgData);
        if (query.Context.Length > MaximumContextLength)
        {
            throw new InvalidDataException($"Profile context length {query.Context.Length} is too long");
        }

        var queried = await FindQueriedRoleAsync(query.QueryUid);
        var response = new pbSCGetAcntProfileNewRsp
        {
            ResultInfo = new pbResultInfo(),
            Context = query.Context,
        };
        if (queried is null)
        {
            logger.LogInformation("Profile of unknown uid {QueryUid} requested by uid {Uid}", query.QueryUid, request.Uid);
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }
        else
        {
            var style = await profiles.LoadAsync(queried.GameUlongUID);
            response.ProfileDetail = await profiles.BuildProfileDetailAsync(queried, style);
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAcntProfileNewRsp, EmptyChildren.Fill(response))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidProfileImageInfoReq)]
    public async Task<GamePacket[]> OnProfileImageInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSProfileImageInfoReq.Parser.ParseFrom(request.PkgData);
        var queried = await FindQueriedRoleAsync(query.QueryUid);
        if (queried is null)
        {
            logger.LogInformation("Profile image of unknown uid {QueryUid} requested by uid {Uid}", query.QueryUid, request.Uid);
            var refusal = new pbSCProfileImageInfoRsp
            {
                ResultInfo = Result(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid),
                QueryUid = query.QueryUid,
            };
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidProfileImageInfoRsp, EmptyChildren.Fill(refusal))];
        }

        var style = await profiles.LoadAsync(queried.GameUlongUID);
        var response = ProfileManager.BuildProfileImageInfo(queried, style);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidProfileImageInfoRsp, EmptyChildren.Fill(response))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidProfileAvatarSyncReq)]
    public GamePacket[] OnProfileAvatarSyncReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSProfileAvatarSyncReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCProfileAvatarSyncRsp
        {
            ResultInfo = new pbResultInfo(),
            RoleAvatar = role.RoleAvatarInfo.Clone(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidProfileAvatarSyncRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidContestantCardEditReq)]
    public async Task<GamePacket[]> OnContestantCardEditReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var info = pbCSContestantCardEditReq.Parser.ParseFrom(request.PkgData).PlayerContestantInfo;
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = profiles.EditContestantCard(role, style, info);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await profiles.SaveAsync(role.GameUlongUID, style);
            logger.LogInformation("Trainer card saved for uid {Uid}", role.GameUlongUID);
        }
        else
        {
            logger.LogInformation("Trainer card edit refused for uid {Uid}: {Result}", role.GameUlongUID, result);
        }

        var response = new pbSCContestantCardEditRsp
        {
            ResultInfo = Result(result),
            PlayerContestantInfo = ProfileManager.BuildPlayerContestantInfo(role, style),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidContestantCardEditRsp, EmptyChildren.Fill(response))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearContestantCardResNewTagReq)]
    public async Task<GamePacket[]> OnClearContestantCardResNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var resId = pbCSClearContestantCardResNewTagReq.Parser.ParseFrom(request.PkgData).ContestantCardResId;
        var result = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        if (!role.TryClearOwnedNewTag(RoleItemType.ContestantCardDecoration, resId, out var cleared))
        {
            logger.LogInformation("Trainer card new tag clear refused for uid {Uid}: {ResId} not owned", role.GameUlongUID, resId);
            result = PB_ENUM_RES_ERROR_CODE.PbErrContestantResNotExist;
        }
        else if (cleared.Length > 0)
        {
            await roles.SaveAsync(role);
        }

        var response = new pbSCClearContestantCardResNewTagRsp
        {
            ResultInfo = Result(result),
            ContestantCardResId = resId,
        };
        GamePacket[] redDot = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? [BuildEntrycardEditItemRedDot(request, role)] : [];
        return [.. redDot, request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearContestantCardResNewTagRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearContestantCardResNewTagByTypeReq)]
    public async Task<GamePacket[]> OnClearContestantCardResNewTagByTypeReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var decorationType = pbCSClearContestantCardResNewTagByTypeReq.Parser.ParseFrom(request.PkgData).Type;
        var response = new pbSCClearContestantCardResNewTagByTypeRsp
        {
            ResultInfo = new pbResultInfo(),
            ChangeContesttantInfo = new pbCOMDT_PLAYER_CONTESTANT_RES_LIST(),
        };
        if (!ContestantCard.IsDecorationType(decorationType))
        {
            logger.LogInformation("Trainer card new tag clear refused for uid {Uid}: type {Type}", role.GameUlongUID, decorationType);
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearContestantCardResNewTagByTypeRsp, response)];
        }

        var ownedDecorationIDs = profiles.FindOwnedDecorationIDs(role, decorationType);
        if (role.ClearNewTags(RoleItemType.ContestantCardDecoration, ownedDecorationIDs).Length > 0)
        {
            await roles.SaveAsync(role);
        }

        response.ChangeContesttantInfo.ResInfoList.Add(ownedDecorationIDs.Order().Select(resID => new pbCOMDT_CONTESTANT_RES_INFO
        {
            ResID = resID,
        }));
        return
        [
            BuildEntrycardEditItemRedDot(request, role),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearContestantCardResNewTagByTypeRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetAnonymityReq)]
    public async Task<GamePacket[]> OnSetAnonymityReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var isAnonymity = pbCSSetAnonymityReq.Parser.ParseFrom(request.PkgData).IsAnonymity;
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        if (ProfileManager.IsFlag(isAnonymity))
        {
            style.IsAnonymity = isAnonymity;
            await profiles.SaveAsync(role.GameUlongUID, style);
            result = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        }

        logger.LogInformation("Anonymity {IsAnonymity} for uid {Uid}: {Result}", isAnonymity, role.GameUlongUID, result);
        var response = new pbSCSetAnonymityRsp { ResultInfo = Result(result), IsAnonymity = style.IsAnonymity };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetAnonymityRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetHideInfoReq)]
    public async Task<GamePacket[]> OnSetHideInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var isHideInfo = pbCSSetHideInfoReq.Parser.ParseFrom(request.PkgData).IsHideInfo;
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        if (ProfileManager.IsFlag(isHideInfo))
        {
            style.IsHideInfo = isHideInfo;
            await profiles.SaveAsync(role.GameUlongUID, style);
            result = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        }

        logger.LogInformation("Hide info {IsHideInfo} for uid {Uid}: {Result}", isHideInfo, role.GameUlongUID, result);
        var response = new pbSCSetHideInfoRsp { ResultInfo = Result(result), IsHideInfo = style.IsHideInfo };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetHideInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetIndividualLabelInfoReq)]
    public async Task<GamePacket[]> OnSetIndividualLabelInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var labelList = pbCSSetIndividualLabelInfoReq.Parser.ParseFrom(request.PkgData).LableInfo?.LabelList ?? [];
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var result = profiles.SetIndividualLabelInfo(style, labelList);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await profiles.SaveAsync(role.GameUlongUID, style);
        }

        logger.LogInformation(
            "Profile labels {LabelList} for uid {Uid}: {Result}",
            string.Join(',', labelList),
            role.GameUlongUID,
            result);
        var response = new pbSCSetIndividualLabelInfoRsp { ResultInfo = Result(result), LableInfo = style.BuildLableInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetIndividualLabelInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRecentVisitorListReq)]
    public GamePacket[] OnRecentVisitorListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSRecentVisitorListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCRecentVisitorListRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRecentVisitorListRsp, response)];
    }

    private static GamePacket BuildEntrycardEditItemRedDot(GamePacket request, LMNRoleInfo role)
    {
        var (msgID, body) = LobbyNotices.BuildRedDotCountNtf(
            PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewEntrycardEditItem,
            role.CountNewTags(RoleItemType.ContestantCardDecoration));
        return request.Reply(msgID, body);
    }

    private static pbResultInfo Result(PB_ENUM_RES_ERROR_CODE result) => new() { ResultId = (int)result };

    private async Task<LMNRoleInfo?> FindQueriedRoleAsync(ulong queryUid) =>
        queryUid == 0 ? null : await roles.FindAsync(queryUid);
}
