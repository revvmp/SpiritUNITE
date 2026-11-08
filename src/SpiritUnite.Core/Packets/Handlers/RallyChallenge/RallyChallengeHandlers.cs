using SpiritUnite.Game.Role;
using SpiritUnite.Game.RallyChallenge;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.RallyChallenge;

public sealed class RallyChallengeHandlers(RallyChallengeManager rallyChallenge, RoleInfoStore roles)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetUniteChallengeInfoReq)]
    public async Task<GamePacket[]> OnGetUniteChallengeInfoReq(GamePacket request)
    {
        pbCSGetUniteChallengeInfoReq.Parser.ParseFrom(request.PkgData);
        var response = await rallyChallenge.BuildInfoRspAsync(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetUniteChallengeInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUniteChallengeStartReq)]
    public async Task<GamePacket[]> OnUniteChallengeStartReq(GamePacket request)
    {
        pbCSUniteChallengeStartReq.Parser.ParseFrom(request.PkgData);
        var result = await rallyChallenge.StartAsync(request.Uid);
        var response = new pbSCUniteChallengeStartRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUniteChallengeStartRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidUniteChallengeResetReq)]
    public async Task<GamePacket[]> OnUniteChallengeResetReq(GamePacket request)
    {
        pbCSUniteChallengeResetReq.Parser.ParseFrom(request.PkgData);
        var result = await rallyChallenge.ResetAsync(request.Uid);
        var response = new pbSCUniteChallengeResetRsp { ResultInfo = new pbResultInfo { ResultId = (int)result } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUniteChallengeResetRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetUniteChallengeRewardReq)]
    public async Task<GamePacket[]> OnGetUniteChallengeRewardReq(GamePacket request)
    {
        var rewardIndex = pbCSGetUniteChallengeRewardReq.Parser.ParseFrom(request.PkgData).RewardIndex;
        var outcome = await rallyChallenge.ClaimRewardAsync(roles.Require(request.Uid), rewardIndex);
        var response = new pbSCGetUniteChallengeRewardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            RewardIndex = rewardIndex,
        };
        return
        [
            .. outcome.BeforeRsp.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetUniteChallengeRewardRsp, response),
        ];
    }
}
