using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed class ActivityTasks(
    ActivitySchedule schedule,
    ActivityRepository repository,
    RoleRepository roleRepository,
    BagManager bag,
    GameData gameData,
    TimeProvider clock,
    ILogger<ActivityTasks> logger)
{
    private const int NoCondition = 0;
    private const int GrantableRewardCount = 1;
    private const int DaysPerWeek = 7;
    private const int CompletedOnce = 1;
    private const int NotCompleted = 0;
    private const int UntrackedProcess = 0;
    private const long NotCompletedTime = 0;

    public Task RecordLoginDayAsync(ulong uid) =>
        repository.RecordLoginDayAsync(uid, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role)
    {
        var now = clock.GetUtcNow();
        var tasks = schedule.FindActiveTasks(now).Where(HasConditions).OrderBy(task => task.Task.TaskID).ToArray();
        var progress = await LoadProgressAsync(role, tasks, now);
        return [.. tasks.Select(task => progress.Evaluate(task).ToTaskInfo())];
    }

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role, PB_ENUM_RES_CONDITION_ID conditionId)
    {
        var now = clock.GetUtcNow();
        var tasks = schedule.FindActiveTasks(now)
            .Where(task => task.Task.FinishCondition.Any(condition => condition.ID == (int)conditionId))
            .OrderBy(task => task.Task.TaskID)
            .ToArray();
        var progress = await LoadProgressAsync(role, tasks, now);
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

        var progress = await LoadProgressAsync(role, [task], now);
        var claim = await ClaimEvaluatedAsync(role, progress.Evaluate(task), now);
        return claim.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            ? await BuildClaimedAsync(role, [claim.State!.ToTaskInfo()], [claim.Grant!])
            : ActivityTaskClaim.Refused(claim.Result);
    }

    public async Task<ActivityTaskClaim> ClaimGroupAsync(LMNRoleInfo role, int groupId)
    {
        var now = clock.GetUtcNow();
        var tasks = schedule.FindActiveTasksInGroup(groupId, now).Where(HasConditions).ToArray();
        var progress = await LoadProgressAsync(role, tasks, now);
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
        var grants = new List<ActivityTaskGrantResult>();
        var lastRefusal = PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        foreach (var state in completed)
        {
            var claim = await ClaimEvaluatedAsync(role, state, now);
            if (claim.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                claimed.Add(claim.State!.ToTaskInfo());
                grants.Add(claim.Grant!);
            }
            else
            {
                lastRefusal = claim.Result;
            }
        }

        return claimed.Count > 0 ? await BuildClaimedAsync(role, [.. claimed], grants) : ActivityTaskClaim.Refused(lastRefusal);
    }

    private async Task<ActivityTaskClaim> BuildClaimedAsync(
        LMNRoleInfo role,
        pbCOMDT_TASK_INFO[] claimed,
        IReadOnlyList<ActivityTaskGrantResult> grants)
    {
        var completeCount = (await BuildTaskInfoListAsync(role))
            .Count(task => task.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);
        var lastCommit = grants[^1].Commit;
        return new ActivityTaskClaim(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            claimed,
            completeCount,
            grants.Any(grant => grant.Grant.Change.CoinGrants.Keys.Any(CoinID.WalletCoins.Contains)) ? lastCommit.Wallet : null,
            [.. grants.SelectMany(grant => grant.Commit.ChangedItems)
                .GroupBy(item => (item.ItemType, item.ItemId))
                .Select(group => group.Last())],
            [.. grants.SelectMany(grant => grant.Grant.Awards)]);
    }

    private async Task<(PB_ENUM_RES_ERROR_CODE Result, ActivityTaskState? State, ActivityTaskGrantResult? Grant)> ClaimEvaluatedAsync(
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
            return (result, null, null);
        }

        var drops = schedule.FindDrops(state.Task.SDropID).ToArray();
        var rewardTime = now.ToUnixTimeSeconds();
        var grant = BuildGrant(role, drops, rewardTime);
        if (grant is null)
        {
            logger.LogInformation(
                "Activity task {TaskId} claim refused for uid {Uid}: a reward of drop {SDropID} has no ownership path",
                taskId, uid, state.Task.SDropID);
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, null, null);
        }

        var reward = new ActivityTaskReward(taskId, state.PeriodStart, rewardTime);
        var commit = await repository.CommitTaskRewardAsync(uid, reward, grant.Items, grant.Change);
        if (commit.Wallet is null)
        {
            logger.LogInformation("Activity task {TaskId} claim refused for uid {Uid}: {Result}", taskId, uid, commit.Result);
            return (commit.Result, null, null);
        }

        foreach (var item in grant.Items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }

        logger.LogInformation(
            "Activity task {TaskId} claimed by uid {Uid}: {RewardCount} rewards granted",
            taskId, uid, drops.Length);
        var claimed = state with
        {
            Status = PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded,
            RewardTime = rewardTime,
        };
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, claimed, new ActivityTaskGrantResult(grant, commit));
    }

    private ActivityTaskGrant? BuildGrant(LMNRoleInfo role, pbDrop_Static[] drops, long rewardTime)
    {
        var items = new List<RoleItemInfo>();
        var coins = new Dictionary<int, long>();
        var bagGrants = new List<BagGrant>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var drop in drops)
        {
            if (drop.ResNum <= 0)
            {
                return null;
            }

            if (CoinID.WalletCoins.Contains(drop.ResID) || CoinID.IsSpecialCoin(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(Award(drop.ResID, drop.ResNum, CoinID.IsSpecialCoin(drop.ResID)
                    ? PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr
                    : PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin));
            }
            else if (drop.ResNum == GrantableRewardCount && gameData.LobbyBg.Contains(drop.ResID))
            {
                if (role.OwnsLobbyBg(gameData, drop.ResID))
                {
                    return null;
                }

                items.Add(new RoleItemInfo(RoleItemType.LobbyBg, drop.ResID, rewardTime, RoleItemInfo.Permanent, RoleItemInfo.ShowNewTag));
            }
            else if (bag.CreateOutSideItemGrant(drop.ResID, drop.ResNum) is { } bagGrant)
            {
                bagGrants.Add(bagGrant);
                awards.Add(Award(drop.ResID, drop.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
            }
            else
            {
                return null;
            }
        }

        return new ActivityTaskGrant(items, new BagChange { Grants = bagGrants, CoinGrants = coins }, awards);
    }

    private static pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO Award(int resID, int resCnt, PB_ENUM_RES_RESOURCES_TYPE resType) => new()
    {
        FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO { ResID = resID, ResCnt = resCnt, ResType = (int)resType },
        OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
    };

    private async Task<ActivityTaskProgress> LoadProgressAsync(
        LMNRoleInfo role,
        IReadOnlyCollection<ActiveActivityTask> tasks,
        DateTimeOffset now)
    {
        var uid = role.GameUlongUID;
        var periods = tasks.ToDictionary(task => task.Task.TaskID, task => FindPeriodStart(task, now));
        var earliest = periods.Count > 0 ? periods.Values.Min() : now;
        var loginDays = await repository.FindLoginDaysAsync(uid, DateOnly.FromDateTime(earliest.UtcDateTime));
        var rewards = await repository.FindTaskRewardsAsync(uid, earliest.ToUnixTimeSeconds());
        var roleCreateTime = await roleRepository.FindCreateTimeAsync(uid);
        return new ActivityTaskProgress(periods, loginDays, rewards, RoleProfile.GetLevel(gameData, role), roleCreateTime);
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
        ActivityTaskReward[] rewards,
        int trainerLevel,
        long roleCreateTime)
    {
        public ActivityTaskState Evaluate(ActiveActivityTask task)
        {
            var periodStart = periodStarts[task.Task.TaskID];
            var firstCountedDay = DateOnly.FromDateTime(periodStart.UtcDateTime);
            var countedLoginDays = loginDays.Where(day => day >= firstCountedDay).Order().ToArray();
            var conditions = task.Task.FinishCondition.Where(condition => condition.ID != NoCondition).ToArray();
            var process = conditions
                .Select(condition => Math.Min(MeasureProcess(condition, countedLoginDays.Length), condition.TargetProcess))
                .ToArray();
            var complete = conditions.All(IsMeasured)
                && conditions.Zip(process).All(pair => pair.Second >= pair.First.TargetProcess);
            var periodStartSeconds = periodStart.ToUnixTimeSeconds();
            var recvTime = Math.Max(periodStartSeconds, roleCreateTime);
            var reward = rewards.FirstOrDefault(reward =>
                reward.TaskId == task.Task.TaskID && reward.PeriodStart == periodStartSeconds);
            var status = reward is not null
                ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
                : complete
                    ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete
                    : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved;
            var completeTime = status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved
                ? NotCompletedTime
                : FindCompleteTime(conditions, countedLoginDays, recvTime);
            return new ActivityTaskState(
                task.Task,
                periodStartSeconds,
                process,
                status,
                recvTime,
                completeTime,
                reward?.RewardTime ?? 0);
        }

        private int MeasureProcess(pbFinishCondition condition, int loginDayCount) =>
            (PB_ENUM_RES_CONDITION_ID)condition.ID switch
            {
                PB_ENUM_RES_CONDITION_ID.PbResConditionIdEverydayLogin => loginDayCount,
                PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach => trainerLevel,
                _ => UntrackedProcess,
            };

        private static long FindCompleteTime(pbFinishCondition[] conditions, DateOnly[] countedLoginDays, long recvTime)
        {
            var neededLoginDays = conditions.Where(IsEverydayLogin).Select(condition => condition.TargetProcess).DefaultIfEmpty(0).Max();
            if (neededLoginDays == 0 || neededLoginDays > countedLoginDays.Length)
            {
                return recvTime;
            }

            var completingDay = countedLoginDays[neededLoginDays - 1].ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            return Math.Max(recvTime, new DateTimeOffset(completingDay).ToUnixTimeSeconds());
        }

        private static bool IsMeasured(pbFinishCondition condition) =>
            IsEverydayLogin(condition)
            || condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach;

        private static bool IsEverydayLogin(pbFinishCondition condition) =>
            condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdEverydayLogin;
    }

    private sealed record ActivityTaskState(
        pbTask_Base Task,
        long PeriodStart,
        int[] Process,
        PB_ENUM_ENM_TASKSTATUS Status,
        long RecvTime,
        long CompleteTime,
        long RewardTime)
    {
        public pbCOMDT_TASK_INFO ToTaskInfo() => new()
        {
            TaskId = Task.TaskID,
            TaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemActivity,
            TaskProcess = { Process },
            Status = (int)Status,
            RecvTime = RecvTime,
            CompleteTime = CompleteTime,
            RewardTime = RewardTime,
            CompleteNum = Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved ? NotCompleted : CompletedOnce,
        };
    }
}

public sealed record ActivityTaskReward(int TaskId, long PeriodStart, long RewardTime);

public sealed record ActivityTaskGrant(
    IReadOnlyList<RoleItemInfo> Items,
    BagChange Change,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards);

public sealed record ActivityTaskGrantResult(ActivityTaskGrant Grant, ActivityRewardCommit Commit);

public sealed record ActivityTaskClaim(
    PB_ENUM_RES_ERROR_CODE Result,
    pbCOMDT_TASK_INFO[] UpdatedTasks,
    int CompleteTaskCount,
    RoleWallet? Wallet,
    IReadOnlyList<BagItem> ChangedItems,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards)
{
    public static ActivityTaskClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, [], 0, null, [], []);
}
