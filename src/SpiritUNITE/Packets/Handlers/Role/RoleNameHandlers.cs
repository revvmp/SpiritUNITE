using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Role;

public sealed class RoleNameHandlers(RoleInfoStore roles, ILogger<RoleNameHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGenerateNewNameSuffixReq)]
    public GamePacket[] OnGenerateNewNameSuffixReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGenerateNewNameSuffixReq.Parser.ParseFrom(request.PkgData);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGenerateNewNameSuffixRsp, new pbSCGenerateNewNameSuffixRsp
            {
                ResultInfo = new pbResultInfo(),
                NameSuffix = TmpNameManager.GenerateNewNameSuffix(),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoleNameChgMsgHasKnowReq)]
    public GamePacket[] OnRoleNameChgMsgHasKnowReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSRoleNameChgMsgHasKnowReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation("Uid {Uid} acknowledged a role name change notice", role.GameUlongUID);
        return [];
    }
}
