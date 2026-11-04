using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed class ActivityTasks(
    ActivitySchedule schedule,
    ActivityRepository repository,
    RoleInfoStore roles,
    GameData gameData,
    TimeProvider clock,
    ILogger<ActivityTasks> logger)
{
    private const int NoCondition = 0;
    private const int GrantableRewardCount = 1;
    private const int DaysPerWeek = 7;
    private const int ClaimedOnce = 1;
    private const int NotClaimed = 0;

    public Task RecordLoginDayAsync(ulong uid) =>
        repository.RecordLoginDayAsync(uid, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(ulong uid)
    {
        var now = clock.GetUtcNow();
        var tasks = schedule.FindActiveTasks(now).Where(HasConditions).ToArray();
        var progress = await LoadProgressAsync(uid, tasks, now);
        return [.. tasks.Select(task => progress.Evaluate(task).ToTaskInfo())];
    }

    public async Task<ActivityTaskClaim> ClaimAsync(LMNRoleInfo role, int taskId)
    {
        var now = clock.GetUtcNow();
        var task = schedule.FindActiveTask(taskId, now);
        if (task is null || !HasConditions(task))
        {
            var result = schedule.TaskBase.Contains(taskId)
                ? PB_ENUM_RES_ERROR_CODE.PbErrTaskExpired
                : PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist;
            logger.LogInformation("Activity task {TaskId} claim refused for uid {Uid}: {Result}", taskId, role.GameUlongUID, result);
            return ActivityTaskClaim.Refused(result);
        }

        var progress = await LoadProgressAsync(role.GameUlongUID, [task], now);
        var claim = await ClaimEvaluatedAsync(role, progress.Evaluate(task), now);
        return claim.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? ActivityTaskClaim.Claimed([claim.State!.ToTaskInfo()])
            : ActivityTaskClaim.Refused(claim.Result);
    }

    public async Task<ActivityTaskClaim> ClaimGroupAsync(LMNRoleInfo role, int groupId)
    {
        var now = clock.GetUtcNow();
        var tasks = schedule.FindActiveTasksInGroup(groupId, now).Where(HasConditions).ToArray();
        var progress = await LoadProgressAsync(role.GameUlongUID, tasks, now);
        var completed = tasks
            .Select(progress.Evaluate)
            .Where(state => state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete)
            .ToArray();
        if (completed.Length == 0)
        {
            logger.LogInformation(
                "Activity task group {GroupId} claim refused for uid {Uid}: no completed task",
                groupId, role.GameUlongUID);
            return ActivityTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist);
        }

        var claimed = new List<pbCOMDT_TASK_INFO>();
        var lastRefusal = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        foreach (var state in completed)
        {
            var claim = await ClaimEvaluatedAsync(role, state, now);
            if (claim.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                claimed.Add(claim.State!.ToTaskInfo());
            }
            else
            {
                lastRefusal = claim.Result;
            }
        }

        return claimed.Count > 0 ? ActivityTaskClaim.Claimed([.. claimed]) : ActivityTaskClaim.Refused(lastRefusal);
    }

    private async Task<(PB_ENUM_RES_ERROR_CODE Result, ActivityTaskState? State)> ClaimEvaluatedAsync(
        LMNRoleInfo role,
        ActivityTaskState state,
        DateTimeOffset now)
    {
        var uid = role.GameUlongUID;
        var taskId = state.Task.TaskID;
        if (state.Status != PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete)
        {
            var result = state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
                ? PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked
                : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            logger.LogInformation("Activity task {TaskId} claim refused for uid {Uid}: {Result}", taskId, uid, result);
            return (result, null);
        }

        var drops = schedule.FindDrops(state.Task.SDropID).ToArray();
        var ungrantable = drops.FirstOrDefault(drop => !CanGrant(role, drop));
        if (ungrantable is not null)
        {
            logger.LogInformation(
                "Activity task {TaskId} claim refused for uid {Uid}: reward {ResID} x{ResNum} has no ownership path",
                taskId, uid, ungrantable.ResID, ungrantable.ResNum);
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, null);
        }

        var rewardTime = now.ToUnixTimeSeconds();
        var reward = new ActivityTaskReward(taskId, state.PeriodStart, rewardTime);
        if (!await repository.TryRecordTaskRewardAsync(uid, reward))
        {
            logger.LogInformation("Activity task {TaskId} claim refused for uid {Uid}: already claimed", taskId, uid);
            return (PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked, null);
        }

        try
        {
            var granted = drops.Count(drop => role.GrantItem(RoleItemType.LobbyBg, drop.ResID, rewardTime) is not null);
            if (granted > 0)
            {
                await roles.SaveAsync(role);
            }
        }
        catch
        {
            await repository.RemoveTaskRewardAsync(uid, reward);
            throw;
        }

        logger.LogInformation(
            "Activity task {TaskId} claimed by uid {Uid}: {RewardCount} rewards granted",
            taskId, uid, drops.Length);
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, state with
        {
            Status = PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded,
            RewardTime = rewardTime,
        });
    }

    private bool CanGrant(LMNRoleInfo role, pbDrop_Static drop) =>
        drop.ResNum == GrantableRewardCount
        && gameData.LobbyBg.Contains(drop.ResID)
        && !role.OwnsLobbyBg(gameData, drop.ResID);

    private async Task<ActivityTaskProgress> LoadProgressAsync(
        ulong uid,
        IReadOnlyCollection<ActiveActivityTask> tasks,
        DateTimeOffset now)
    {
        var periods = tasks.ToDictionary(task => task.Task.TaskID, task => FindPeriodStart(task, now));
        var earliest = periods.Count > 0 ? periods.Values.Min() : now;
        var loginDays = await repository.FindLoginDaysAsync(uid, DateOnly.FromDateTime(earliest.UtcDateTime));
        var rewards = await repository.FindTaskRewardsAsync(uid, earliest.ToUnixTimeSeconds());
        return new ActivityTaskProgress(periods, loginDays, rewards);
    }

    private static DateTimeOffset FindPeriodStart(ActiveActivityTask task, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var periodStart = (PB_ENUM_RES_TASK_RESETTYPE)task.Task.ResetType switch
        {
            PB_ENUM_RES_TASK_RESETTYPE.PbResTaskResettypeDaily => today,
            PB_ENUM_RES_TASK_RESETTYPE.PbResTaskResettypeWeek =>
                today.AddDays(-(((int)today.DayOfWeek + DaysPerWeek - (int)DayOfWeek.Monday) % DaysPerWeek)),
            _ => task.Activity.ValidStart,
        };
        return periodStart > task.Activity.ValidStart ? periodStart : task.Activity.ValidStart;
    }

    private static bool HasConditions(ActiveActivityTask task) =>
        task.Task.FinishCondition.Any(condition => condition.ID != NoCondition);

    private sealed class ActivityTaskProgress(
        Dictionary<int, DateTimeOffset> periodStarts,
        DateOnly[] loginDays,
        ActivityTaskReward[] rewards)
    {
        public ActivityTaskState Evaluate(ActiveActivityTask task)
        {
            var periodStart = periodStarts[task.Task.TaskID];
            var firstCountedDay = DateOnly.FromDateTime(periodStart.UtcDateTime);
            var loginDayCount = loginDays.Count(day => day >= firstCountedDay);
            var conditions = task.Task.FinishCondition.Where(condition => condition.ID != NoCondition).ToArray();
            var process = conditions
                .Select(condition => IsEverydayLogin(condition) ? Math.Min(loginDayCount, condition.TargetProcess) : 0)
                .ToArray();
            var complete = conditions.All(IsEverydayLogin)
                && conditions.Zip(process).All(pair => pair.Second >= pair.First.TargetProcess);
            var periodStartSeconds = periodStart.ToUnixTimeSeconds();
            var reward = rewards.FirstOrDefault(reward =>
                reward.TaskId == task.Task.TaskID && reward.PeriodStart == periodStartSeconds);
            var status = reward is not null
                ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
                : complete
                    ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete
                    : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved;
            return new ActivityTaskState(task.Task, periodStartSeconds, process, status, reward?.RewardTime ?? 0);
        }

        private static bool IsEverydayLogin(pbFinishCondition condition) =>
            condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdEverydayLogin;
    }

    private sealed record ActivityTaskState(
        pbTask_Base Task,
        long PeriodStart,
        int[] Process,
        PB_ENUM_ENM_TASKSTATUS Status,
        long RewardTime)
    {
        public pbCOMDT_TASK_INFO ToTaskInfo() => new()
        {
            TaskId = Task.TaskID,
            TaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemActivity,
            TaskProcess = { Process },
            Status = (int)Status,
            RecvTime = PeriodStart,
            RewardTime = RewardTime,
            CompleteNum = Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded ? ClaimedOnce : NotClaimed,
        };
    }
}

public sealed record ActivityTaskClaim(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_TASK_INFO[] UpdatedTasks)
{
    public static ActivityTaskClaim Claimed(pbCOMDT_TASK_INFO[] updatedTasks) =>
        new(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, updatedTasks);

    public static ActivityTaskClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}
