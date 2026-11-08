using SpiritUnite.Game.Activity;
using SpiritUnite.Packets.Messages;
using static SpiritUnite.Packets.Messages.PB_ENUM_CS_MSGID_DEF;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class CampBattleHandlers(CampBattleManager campBattle)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidCampBattleActInfoReq)]
    public async Task<GamePacket[]> OnCampBattleActInfoReq(GamePacket request)
    {
        pbCSCampBattleActInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidCampBattleActInfoRsp, await campBattle.InfoAsync(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidCampBattleActJoinReq)]
    public async Task<GamePacket[]> OnCampBattleActJoinReq(GamePacket request)
    {
        var join = pbCSCampBattleActJoinReq.Parser.ParseFrom(request.PkgData);
        var outcome = await campBattle.JoinAsync(request.Uid, join.JoinCamp);
        return
        [
            request.Reply(PbScidCampBattleActJoinRsp, new pbSCCampBattleActJoinRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
                ActAcntInfo = outcome.PersonalInfo,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidCampBattleActAwardReq)]
    public async Task<GamePacket[]> OnCampBattleActAwardReq(GamePacket request)
    {
        pbCSCampBattleActAwardReq.Parser.ParseFrom(request.PkgData);
        var outcome = await campBattle.AwardAsync(request.Uid);
        return
        [
            request.Reply(PbScidCampBattleActAwardRsp, new pbSCCampBattleActAwardRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
                PersonalInfo = outcome.PersonalInfo,
            }),
        ];
    }
}
