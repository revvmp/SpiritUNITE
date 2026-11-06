using SpiritUNITE.Game.PveChallenge;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.PveChallenge;

public sealed class PveChallengeHandlers(RoleInfoStore roles, PveChallengeProgress pveChallenge)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPveChallengeStageInfoReq)]
    public GamePacket[] OnPveChallengeStageInfoReq(GamePacket request)
    {
        var stageID = pbCSPveChallengeStageInfoReq.Parser.ParseFrom(request.PkgData).StageID;
        var role = roles.Require(request.Uid);
        var nodes = pveChallenge.Load(role);
        var response = new pbSCPveChallengeStageInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            PveChallengeInfo = PveChallengeProgress.BuildInfo(nodes),
            PveChallengeBuffInfo = new pbCOMDT_PVE_CHALLENGE_BUFF_INFO(),
            PveChallengeShopInfo = new pbCOMDT_PVE_CHALLENGE_SHOP_INFO(),
            ReqStageID = stageID,
            PrivilegeInfo = new pbCOMDT_PVE_PRIVILEGE_ACTIVITY_INFO(),
        };
        return
        [
            .. PveChallengeProgress.BuildNewbieCompletedNodeNtfs(role, nodes)
                .Select(ntf => request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeStageInfoNtf, ntf)),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPveChallengeStageInfoRsp, response),
        ];
    }
}
