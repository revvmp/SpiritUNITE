using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Role;

public sealed class CosmeticHandlers(
    RoleInfoStore roles,
    GameData gameData,
    TimeProvider clock,
    ILogger<CosmeticHandlers> logger)
{
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

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetLobbyBackgroundInfoReq)]
    public GamePacket[] OnGetLobbyBackgroundInfoReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGetLobbyBackgroundInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetLobbyBackgroundInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            LobbyBackgroundInfo = role.BuildLobbyBackgroundInfo(gameData),
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetLobbyBackgroundInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetRoleAvatarReq)]
    public async Task<GamePacket[]> OnSetRoleAvatarReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var avatarIDList = pbCSSetRoleAvatarReq.Parser.ParseFrom(request.PkgData).RoleAvatarInfo?.AvatarIDList ?? [];
        var result = role.SetRoleAvatar(gameData, avatarIDList, clock.GetUtcNow().ToUnixTimeSeconds());
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

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetRoleAvatarNtf, response)];
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
        var (result, updates) = await ClearAvatarNewTagAsync(request, avatarIDList);
        var response = new pbSCBatchClearAvatarNewTagRsp { Result = new pbResultInfo { ResultId = (int)result } };
        return [.. updates, request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidBatchClearAvatarNewTagRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChgLobbyBackgroundReq)]
    public async Task<GamePacket[]> OnChgLobbyBackgroundReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var lobbyBgId = pbCSChgLobbyBackgroundReq.Parser.ParseFrom(request.PkgData).LobbyBgId;
        var result = role.ChgLobbyBackground(gameData, lobbyBgId);
        if (result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await roles.SaveAsync(role);
            logger.LogInformation("Lobby background {LobbyBgId} set for uid {Uid}", lobbyBgId, role.GameUlongUID);
        }
        else
        {
            logger.LogInformation(
                "Lobby background {LobbyBgId} refused for uid {Uid}: {Result}",
                lobbyBgId,
                role.GameUlongUID,
                result);
        }

        var response = new pbSCChgLobbyBackgroundRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            LobbyBgId = role.GetUsingLobbyBgId(gameData),
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChgLobbyBackgroundRsp, response)];
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

        return (result, [.. cleared.Select(item => request.Reply(
            PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf,
            new pbSCUpdateAvatarNtf
            {
                UpdateType = (int)PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeUpdate,
                AvatarInfo = item.ToAvatarInfo(),
            }))]);
    }
}
