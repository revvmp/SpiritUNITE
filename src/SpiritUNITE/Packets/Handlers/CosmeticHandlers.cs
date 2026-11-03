using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class CosmeticHandlers(RoleInfoStore roles)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAvatarListReq)]
    public GamePacket[] OnGetAvatarListReq(GamePacket request)
    {
        var role = RequireRole(request);
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
        RequireRole(request);
        pbCSGetLobbyBackgroundInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetLobbyBackgroundInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            LobbyBackgroundInfo = LMNRoleInfo.BuildLobbyBackgroundInfo(),
        };

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetLobbyBackgroundInfoRsp, response)];
    }

    private LMNRoleInfo RequireRole(GamePacket request) =>
        roles.Find(request.Uid) ?? throw new InvalidDataException("Message requires a logged in role");
}
