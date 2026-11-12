using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Room;

public static class RoomDailyActivity
{
    private const int AllTasksRewarded = (int)PB_ENUM_ENM_ROOM_DAILY_ACTIVITY_REWARD_TYPE.PbEnmRoomDailyActivityRewardTypeNull;
    private const int RewardWaiting = (int)PB_ENUM_ENM_ROOM_DAILY_ACTIVITY_REWARD_TYPE.PbEnmRoomDailyActivityRewardTypeReward;
    private const int TasksInProgress = (int)PB_ENUM_ENM_ROOM_DAILY_ACTIVITY_REWARD_TYPE.PbEnmRoomDailyActivityRewardTypeTask;
    private const int TaskComplete = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete;
    private const int TaskRewarded = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded;

    private static readonly PB_ENUM_RES_CONDITION_ID[] AnyModeConditions =
    [
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdWin,
    ];

    private static readonly PB_ENUM_RES_CONDITION_ID[] RaceNormalConditions =
    [
        .. AnyModeConditions,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRaceFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRaceWin,
    ];

    private static readonly PB_ENUM_RES_CONDITION_ID[] RaceRankConditions =
    [
        .. AnyModeConditions,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRankedMatchFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRankedMatchWin,
    ];

    private static readonly PB_ENUM_RES_CONDITION_ID[] RelaxNormalConditions =
    [
        .. AnyModeConditions,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRelaxFinish,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdRelaxWin,
    ];

    public static async Task<pbCSDT_ROOM_DAILY_ACTIVITY_INFO> BuildDailyActivityInfoAsync(
        ActivitySchedule schedule,
        ActivityManager activityManager,
        LMNRoleInfo role,
        DateTimeOffset now) =>
        BuildDailyActivityInfo(schedule, now, await activityManager.BuildTaskInfoListAsync(role));

    private static pbCSDT_ROOM_DAILY_ACTIVITY_INFO BuildDailyActivityInfo(
        ActivitySchedule schedule,
        DateTimeOffset now,
        IReadOnlyCollection<pbCOMDT_TASK_INFO> activityTaskInfos)
    {
        var dailyTasks = schedule.FindActive(now)
            .Where(activity => activity.Row.ActivityID == schedule.DailyActivityId)
            .SelectMany(schedule.FindSchemeTaskGroupIds)
            .SelectMany(groupId => schedule.FindActiveTasksInGroup(groupId, now))
            .Select(activeTask => activeTask.Task)
            .DistinctBy(task => task.TaskID)
            .ToArray();
        var statusByTaskId = activityTaskInfos.ToDictionary(task => task.TaskId, task => task.Status);
        return new pbCSDT_ROOM_DAILY_ACTIVITY_INFO
        {
            RaceNormalInfo = BuildSubmodeInfo(dailyTasks, statusByTaskId, RaceNormalConditions),
            RaceRankInfo = BuildSubmodeInfo(dailyTasks, statusByTaskId, RaceRankConditions),
            RelaxNormalInfo = BuildSubmodeInfo(dailyTasks, statusByTaskId, RelaxNormalConditions),
        };
    }

    private static pbCSDT_ROOM_DAILY_ACTIVITY_SUBMODE_INFO BuildSubmodeInfo(
        pbTask_Base[] dailyTasks,
        IReadOnlyDictionary<int, int> statusByTaskId,
        PB_ENUM_RES_CONDITION_ID[] submodeConditions)
    {
        int[] statuses =
        [
            .. dailyTasks
                .Where(task => task.FinishCondition.Any(condition => submodeConditions.Contains((PB_ENUM_RES_CONDITION_ID)condition.ID)))
                .Select(task => statusByTaskId.GetValueOrDefault(task.TaskID)),
        ];
        return new pbCSDT_ROOM_DAILY_ACTIVITY_SUBMODE_INFO
        {
            Status = SubmodeStatus(statuses),
            CompleteTaskNum = statuses.Count(status => status is TaskComplete or TaskRewarded),
            TotalTaskNum = statuses.Length,
        };
    }

    private static int SubmodeStatus(int[] statuses) =>
        statuses.Contains(TaskComplete) ? RewardWaiting
        : statuses.Length > 0 && statuses.All(status => status == TaskRewarded) ? AllTasksRewarded
        : TasksInProgress;
}
