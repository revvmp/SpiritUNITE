using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Activity;

public sealed class MegaEvolutionHandlers(MegaEvolutionManager megaEvolution, RoleInfoStore roles)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMegaEvolutionActivityDataReq)]
    public async Task<GamePacket[]> OnMegaEvolutionActivityDataReq(GamePacket request)
    {
        var activityID = pbCSMegaEvolutionActivityDataReq.Parser.ParseFrom(request.PkgData).ActivityID;
        var outcome = await megaEvolution.BuildDataAsync(request.Uid, activityID);
        var response = new pbSCMegaEvolutionActivityDataRsp { ResultInfo = Result(outcome), ActivityID = activityID, DataInfo = outcome.DataInfo };
        return Replies(request, outcome, PB_ENUM_CS_MSGID_DEF.PbScidMegaEvolutionActivityDataRsp, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMegaEvolutionTrainReq)]
    public async Task<GamePacket[]> OnMegaEvolutionTrainReq(GamePacket request)
    {
        var train = pbCSMegaEvolutionTrainReq.Parser.ParseFrom(request.PkgData);
        var outcome = await megaEvolution.TrainAsync(roles.Require(request.Uid), train.ActivityID, train.TrainNum);
        var response = new pbSCMegaEvolutionTrainRsp
        {
            ResultInfo = Result(outcome),
            ActivityID = train.ActivityID,
            DataInfo = outcome.DataInfo,
            TrainResultList = new pbCSDT_MEGA_EVOLUTION_TRAIN_LIST { TrainResults = { outcome.TrainResults ?? [] } },
        };
        return Replies(request, outcome, PB_ENUM_CS_MSGID_DEF.PbScidMegaEvolutionTrainRsp, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMegaEvolutionSupperTrainReq)]
    public async Task<GamePacket[]> OnMegaEvolutionSupperTrainReq(GamePacket request)
    {
        var train = pbCSMegaEvolutionSupperTrainReq.Parser.ParseFrom(request.PkgData);
        var outcome = await megaEvolution.SuperTrainAsync(roles.Require(request.Uid), train.ActivityID, train.BuyNum);
        var response = new pbSCMegaEvolutionSupperTrainRsp { ResultInfo = Result(outcome), ActivityID = train.ActivityID, DataInfo = outcome.DataInfo };
        return Replies(request, outcome, PB_ENUM_CS_MSGID_DEF.PbScidMegaEvolutionSupperTrainRsp, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMegaEvolutionTaskFinishReq)]
    public async Task<GamePacket[]> OnMegaEvolutionTaskFinishReq(GamePacket request)
    {
        var finish = pbCSMegaEvolutionTaskFinishReq.Parser.ParseFrom(request.PkgData);
        var outcome = await megaEvolution.FinishTaskAsync(roles.Require(request.Uid), finish.ActivityID, finish.TaskType, finish.TaskID);
        var response = new pbSCMegaEvolutionTaskFinishRsp { ResultInfo = Result(outcome), ActivityID = finish.ActivityID };
        return Replies(request, outcome, PB_ENUM_CS_MSGID_DEF.PbScidMegaEvolutionTaskFinishRsp, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMegaEvolutionRewardReq)]
    public async Task<GamePacket[]> OnMegaEvolutionRewardReq(GamePacket request)
    {
        var reward = pbCSMegaEvolutionRewardReq.Parser.ParseFrom(request.PkgData);
        var outcome = await megaEvolution.ClaimRewardAsync(roles.Require(request.Uid), reward.ActivityID, reward.RewardID);
        var response = new pbSCMegaEvolutionRewardRsp { ResultInfo = Result(outcome), ActivityID = reward.ActivityID, DataInfo = outcome.DataInfo };
        return Replies(request, outcome, PB_ENUM_CS_MSGID_DEF.PbScidMegaEvolutionRewardRsp, response);
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMegaEvolutionEnterNextStageReq)]
    public async Task<GamePacket[]> OnMegaEvolutionEnterNextStageReq(GamePacket request)
    {
        var activityID = pbCSMegaEvolutionEnterNextStageReq.Parser.ParseFrom(request.PkgData).ActivityID;
        var outcome = await megaEvolution.EnterNextStageAsync(request.Uid, activityID);
        var response = new pbSCMegaEvolutionEnterNextStageRsp { ResultInfo = Result(outcome), ActivityID = activityID, DataInfo = outcome.DataInfo };
        return Replies(request, outcome, PB_ENUM_CS_MSGID_DEF.PbScidMegaEvolutionEnterNextStageRsp, response);
    }

    private static pbResultInfo Result(MegaEvolutionOutcome outcome) => new() { ResultId = (int)outcome.Result };

    private static GamePacket[] Replies(GamePacket request, MegaEvolutionOutcome outcome, PB_ENUM_CS_MSGID_DEF msgID, Google.Protobuf.IMessage response) =>
    [
        .. outcome.BeforeRsp.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
        request.Reply(msgID, response),
    ];
}
