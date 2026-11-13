using SpiritUnite.Game.Login;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Offline;

public sealed class OfflineHandlers(RoleInfoStore roles, LoginManager login)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidAcntLoginDataReq)]
    public async Task<GamePacket[]> OnAcntLoginDataReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSAcntLoginDataReq.Parser.ParseFrom(request.PkgData);
        var response = await login.BuildAcntLoginDataRspAsync(role);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAcntLoginDataRsp, response)];
    }
}
