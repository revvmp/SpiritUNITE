using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Activity;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.BattlePass;

public sealed class BattlePassActivities(
    ActivitySchedule schedule,
    BattlePassSeasons seasons,
    TimeProvider clock,
    ILogger<BattlePassActivities> logger)
{
    public PB_ENUM_RES_ERROR_CODE RefuseActivityRequest(ulong uid, string action, int activityID)
    {
        var open = schedule.FindActive(clock.GetUtcNow()).Any(activity =>
            activity.Row.ActivityID == activityID
            && activity.Row.ActivityType == (int)PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeBattlePass);
        var result = open
            ? PB_ENUM_RES_ERROR_CODE.PbErrBattlePassActivityGetRewardFail
            : PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit;
        logger.LogInformation(
            "Event battle pass {Action} for activity {ActivityID} from uid {Uid} answered {Result}: the activity is {State}",
            action, activityID, uid, result, open ? "open but event battle passes are not built" : "not open");
        return result;
    }

    public PB_ENUM_RES_ERROR_CODE RefuseFriendGift(ulong uid, string action)
    {
        var current = seasons.FindCurrentSeason(clock.GetUtcNow());
        var result = current.IsInProcess
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed
            : PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState;
        logger.LogInformation(
            "Battle pass friend gift {Action} from uid {Uid} answered {Result}: season {SeasonID} is {SeasonState} and the client has no pass purchase",
            action, uid, result, current.Season.SeasonID, current.SeasonState);
        return result;
    }
}
