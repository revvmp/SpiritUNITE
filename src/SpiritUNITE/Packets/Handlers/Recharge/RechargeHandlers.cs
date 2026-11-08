using SpiritUNITE.Game.Recharge;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Recharge;

public sealed class RechargeHandlers(RoleInfoStore roles, RechargeManager recharge)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCouponsFlowReq)]
    public async Task<GamePacket[]> OnCouponsFlowReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSCouponsFlowReq.Parser.ParseFrom(request.PkgData);
        var response = await recharge.BuildCouponsFlowRspAsync(role.GameUlongUID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCouponsFlowRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidChargeVerCompatibleCheckReq)]
    public GamePacket[] OnChargeVerCompatibleCheckReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSChargeVerCompatibleCheckReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCChargeVerCompatibleCheckRsp { ResultInfo = new pbResultInfo() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidChargeVerCompatibleCheckRsp, response)];
    }
}
