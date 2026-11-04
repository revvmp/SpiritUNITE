using Microsoft.Extensions.Logging;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Role;

public sealed class RoleHandlers(
    RoleInfoStore roles,
    GameData gameData,
    AppearanceGift appearanceGift,
    TaskUpdates taskUpdates,
    TimeProvider clock,
    ILogger<RoleHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetRoleCosmeticReq)]
    public async Task<GamePacket[]> OnSetRoleCosmeticReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var cosmetic = pbCSSetRoleCosmeticReq.Parser.ParseFrom(request.PkgData);
        var roleAvatarInfo = cosmetic.RoleAvatarInfo ?? new pbCOMDT_ROLE_AVATAR_INFO();
        var gainTime = clock.GetUtcNow().ToUnixTimeSeconds();
        var result = role.SetRoleCosmetic(
            gameData,
            cosmetic.Gender,
            roleAvatarInfo,
            gainTime,
            out var grantedSuitItems);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("Role cosmetic refused for uid {Uid}: {Result}", role.GameUlongUID, result);
            return [BuildSetRoleCosmeticRsp(request, role, result)];
        }

        await roles.SaveAsync(role);
        GamePacket[] registGive = grantedSuitItems.Length == 0
            ? []
            :
            [
                BuildRegistGiveAwardNotify(request, grantedSuitItems),
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)),
            ];
        var giftPushes = await appearanceGift.GrantAsync(role, gainTime);
        return
        [
            .. grantedSuitItems.Select(item => BuildUpdateAvatarNtf(request, PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeGain, item)),
            .. registGive,
            .. roleAvatarInfo.AvatarIDList
                .Select(avatarID => grantedSuitItems.FirstOrDefault(item => item.ItemId == avatarID))
                .OfType<RoleItemInfo>()
                .Select(item => BuildUpdateAvatarNtf(request, PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeUpdate, item)),
            .. giftPushes.Select(push => request.Reply(push.MsgID, push.Body)),
            BuildSetRoleCosmeticRsp(request, role, result),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAvatarListReq)]
    public GamePacket[] OnGetAvatarListReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetAvatarListReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetAvatarListRsp
        {
            ResultInfo = new pbResultInfo(),
            AvatarList = { role.BuildAvatarList() },
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAvatarListRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetRoleAvatarReq)]
    public async Task<GamePacket[]> OnSetRoleAvatarReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var avatarIDList = pbCSSetRoleAvatarReq.Parser.ParseFrom(request.PkgData).RoleAvatarInfo?.AvatarIDList ?? [];
        var result = role.SetRoleAvatar(gameData, avatarIDList, clock.GetUtcNow().ToUnixTimeSeconds(), out var wornAvatarItems);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await roles.SaveAsync(role);
            logger.LogInformation(
                "Role avatar set for uid {Uid}: {AvatarIDList}",
                role.GameUlongUID,
                string.Join(',', avatarIDList));
        }
        else
        {
            logger.LogInformation(
                "Role avatar refused for uid {Uid}: {Result} for {Count} ids",
                role.GameUlongUID,
                result,
                avatarIDList.Count);
        }

        var response = new pbSCSetRoleAvatarNtf
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            RoleAvatarInfo = role.RoleAvatarInfo,
        };
        GamePacket[] avatarRedDotClear = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? [request.Reply(
                PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf,
                LobbyNotices.BuildRedDotClearNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdAddAvatar))]
            : [];

        return
        [
            .. wornAvatarItems.Select(item => BuildUpdateAvatarNtf(request, PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeUpdate, item)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetRoleAvatarNtf, response),
            .. avatarRedDotClear,
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearAvatarNewTagReq)]
    public async Task<GamePacket[]> OnClearAvatarNewTagReq(GamePacket request)
    {
        var avatarID = pbCSClearAvatarNewTagReq.Parser.ParseFrom(request.PkgData).AvatarID;
        var (result, updates) = await ClearAvatarNewTagAsync(request, [avatarID]);
        var response = new pbSCClearAvatarNewTagRsp { Result = new pbResultInfo { ResultId = (int)result } };
        return [.. updates, request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearAvatarNewTagRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidBatchClearAvatarNewTagReq)]
    public async Task<GamePacket[]> OnBatchClearAvatarNewTagReq(GamePacket request)
    {
        var avatarIDList = pbCSBatchClearAvatarNewTagReq.Parser.ParseFrom(request.PkgData).AvatarIDList;
        var (_, updates) = await ClearAvatarNewTagAsync(request, avatarIDList);
        return updates;
    }

    private async Task<(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] Updates)> ClearAvatarNewTagAsync(
        GamePacket request,
        IReadOnlyCollection<int> avatarIDList)
    {
        var role = roles.Require(request.Uid);
        var result = role.ClearAvatarNewTag(avatarIDList, out var cleared);
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation(
                "Avatar new tag clear refused for uid {Uid}: {Result} for {Count} ids",
                role.GameUlongUID,
                result,
                avatarIDList.Count);
            return (result, []);
        }

        if (cleared.Length > 0)
        {
            await roles.SaveAsync(role);
        }

        return (result, [.. cleared.Select(item => BuildUpdateAvatarNtf(request, PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeUpdate, item))]);
    }

    private GamePacket BuildSetRoleCosmeticRsp(GamePacket request, LMNRoleInfo role, PB_ENUM_RES_ERROR_CODE result) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetRoleCosmeticRsp, new pbSCSetRoleCosmeticRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            RoleAvatarInfo = role.RoleAvatarInfo,
            AvatarSuitSeqID = role.GetAvatarSuitSeqID(gameData),
            Gender = role.Gender,
            HeadID = RoleImage.GetHeadID(gameData, role),
            UserShortId = RoleProfile.BuildShortId(role),
        });

    private static GamePacket BuildUpdateAvatarNtf(GamePacket request, PB_ENUM_ENM_AVATAR_UPDATETYPE updateType, RoleItemInfo item) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf, new pbSCUpdateAvatarNtf
        {
            UpdateType = (int)updateType,
            AvatarInfo = item.ToAvatarInfo(),
        });

    private static GamePacket BuildRegistGiveAwardNotify(GamePacket request, IEnumerable<RoleItemInfo> items) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonRegistgive,
            ResourcesList =
            {
                items.Select(item => new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
                {
                    FinallyAwardInfo = item.ToResourceAwardInfo(),
                    OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
                }),
            },
        });
}
