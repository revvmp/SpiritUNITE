using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Activity;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Room;

public sealed class ActivityPMTrainerHandlers(ActivitySchedule activities, TimeProvider clock, ILogger<ActivityPMTrainerHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMatchTrainerStatusReq)]
    public GamePacket[] OnMatchTrainerStatusReq(GamePacket request)
    {
        pbCSMatchTrainerStatusReq.Parser.ParseFrom(request.PkgData);
        var activity = activities.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeMatchTrainer, clock.GetUtcNow());
        logger.LogInformation("Uid {Uid} asked for the past trainer status: activity {ActivityID}", request.Uid, activity?.Row.ActivityID);
        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMatchTrainerStatusRsp, activity is null
                ? new pbSCMatchTrainerStatusRsp { ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrMatchTrainerActivityNotExit } }
                : new pbSCMatchTrainerStatusRsp { ResultInfo = new pbResultInfo(), ActivityID = activity.Row.ActivityID }),
        ];
    }
}
