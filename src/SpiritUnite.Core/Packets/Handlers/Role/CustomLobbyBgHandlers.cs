using Microsoft.Extensions.Logging;
using SpiritUnite.Databin;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Role;

public sealed class CustomLobbyBgHandlers(
    RoleInfoStore roles,
    GameData gameData,
    ILogger<CustomLobbyBgHandlers> logger)
{
    private const int LobbyBgUpdateTypeChange = 1;

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

        return
        [
            BuildLobbyBackgroundRedDot(request, role),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetLobbyBackgroundInfoRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearLobbyBackgroundNewTagReq)]
    public async Task<GamePacket[]> OnClearLobbyBackgroundNewTagReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var lobbyBgId = pbCSClearLobbyBackgroundNewTagReq.Parser.ParseFrom(request.PkgData).LobbyBgId;
        var response = new pbSCClearLobbyBackgroundNewTagRsp { ResultInfo = new pbResultInfo(), LobbyBgId = lobbyBgId };
        if (!role.TryClearOwnedNewTag(RoleItemType.LobbyBg, lobbyBgId, out var cleared))
        {
            logger.LogInformation("Lobby background {LobbyBgId} new tag clear refused for uid {Uid}: not owned", lobbyBgId, role.GameUlongUID);
            response.ResultInfo.ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrLobbyBgCanNotUse;
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearLobbyBackgroundNewTagRsp, response)];
        }

        if (cleared.Length > 0)
        {
            await roles.SaveAsync(role);
        }

        var update = new pbSCUpdateLobbyBackgroundNtf
        {
            LobbyBgUpdateList =
            {
                cleared.Select(item => new pbCOMDT_LOBBY_BACKGROUND_UPDATE_NODE
                {
                    UpdateType = LobbyBgUpdateTypeChange,
                    LobbyBgData = item.ToLobbyBackgroundData(),
                }),
            },
        };
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateLobbyBackgroundNtf, update),
            BuildLobbyBackgroundRedDot(request, role),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearLobbyBackgroundNewTagRsp, response),
        ];
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

    private static GamePacket BuildLobbyBackgroundRedDot(GamePacket request, LMNRoleInfo role)
    {
        var (msgID, body) = LobbyNotices.BuildRedDotCountNtf(
            PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdLobbyBackground,
            role.CountNewTags(RoleItemType.LobbyBg));
        return request.Reply(msgID, body);
    }
}
