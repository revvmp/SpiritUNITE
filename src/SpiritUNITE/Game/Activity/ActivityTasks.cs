using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed class ActivityTasks(
    ActivitySchedule schedule,
    ActivityRepository repository,
    TaskProcessRepository taskProcesses,
    RoleRepository roleRepository,
    EeveeChallengeRepository eeveeChallenges,
    EeveeChallengePool eeveePool,
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
    private const int MaxChainRounds = 8;

    public Task RecordLoginDayAsync(ulong uid) =>
        repository.RecordLoginDayAsync(uid, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role)
    {
        var now = clock.GetUtcNow();
        var tasks = (await FindRoleTasksAsync(role, now)).Where(HasConditions).OrderBy(task => task.Task.TaskID).ToArray();
        var progress = await LoadProgressAsync(role, tasks, now);
        return [.. tasks.Select(task => progress.Evaluate(task).ToTaskInfo())];
    }

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role, IReadOnlyCollection<int> taskIds)
    {
        var now = clock.GetUtcNow();
        var tasks = (await FindRoleTasksAsync(role, now))
            .Where(task => taskIds.Contains(task.Task.TaskID) && HasConditions(task))
            .OrderBy(task => task.Task.TaskID)
            .ToArray();
        var progress = await LoadProgressAsync(role, tasks, now);
        return [.. tasks.Select(task => progress.Evaluate(task).ToTaskInfo())];
    }

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role, PB_ENUM_RES_CONDITION_ID conditionId)
    {
        var now = clock.GetUtcNow();
        var tasks = (await FindRoleTasksAsync(role, now))
            .Where(task => task.Task.FinishCondition.Any(condition => condition.ID == (int)conditionId))
            .OrderBy(task => task.Task.TaskID)
            .ToArray();
        var progress = await LoadProgressAsync(role, tasks, now);
        return [.. tasks.Select(task => progress.Evaluate(task).ToTaskInfo())];
    }

    public async Task<int> CountCompleteTasksAsync(LMNRoleInfo role) =>
        (await BuildTaskInfoListAsync(role)).Count(task => task.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);

    public async Task<ActivityBattleProgress> RecordBattleAsync(LMNRoleInfo role, BattleTaskEvent battle)
    {
        var now = clock.GetUtcNow();
        var tasks = (await FindRoleTasksAsync(role, now))
            .Where(HasConditions)
            .DistinctBy(task => task.Task.TaskID)
            .OrderBy(task => task.Task.TaskID)
            .ToArray();
        var before = await LoadProgressAsync(role, tasks, now);
        var completeCountBefore = tasks.Count(task => before.Evaluate(task).Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);
        var increments = tasks.Select(task => FindBattleIncrement(task, battle, now)).OfType<TaskProcessIncrement>().ToArray();
        if (increments.Length == 0)
        {
            return new ActivityBattleProgress([], completeCountBefore, completeCountBefore);
        }

        if (!await taskProcesses.AddAsync(role.GameUlongUID, increments, now.ToUnixTimeSeconds()))
        {
            logger.LogWarning("Battle task progress of uid {Uid} was not stored: the role row is missing", role.GameUlongUID);
            return new ActivityBattleProgress([], completeCountBefore, completeCountBefore);
        }

        var after = await LoadProgressAsync(role, tasks, now);
        var completeCountAfter = tasks.Count(task => after.Evaluate(task).Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);
        var autoClaim = await ClaimCompletedAutoClaimTasksAsync(role, tasks.Where(IsAutoClaimed).Select(after.Evaluate), now);
        var final = autoClaim is null ? after : await LoadProgressAsync(role, tasks, now);
        var touchedTaskIds = increments.Select(increment => increment.TaskId).ToHashSet();
        return new ActivityBattleProgress(
            [.. tasks.Where(task => touchedTaskIds.Contains(task.Task.TaskID)).Select(task => final.Evaluate(task).ToTaskInfo())],
            completeCountBefore,
            completeCountAfter,
            autoClaim);
    }

    private async Task<ActivityTaskClaim?> ClaimCompletedAutoClaimTasksAsync(LMNRoleInfo role, IEnumerable<ActivityTaskState> states, DateTimeOffset now)
    {
        var claimed = new List<pbCOMDT_TASK_INFO>();
        var grants = new List<ActivityTaskGrantResult>();
        foreach (var state in states.Where(state => state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete))
        {
            var claim = await ClaimEvaluatedAsync(role, state, now);
            if (claim.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                claimed.Add(claim.State!.ToTaskInfo());
                grants.Add(claim.Grant!);
            }
        }

        return claimed.Count == 0 ? null : await BuildClaimedAsync(role, [.. claimed], grants, now);
    }

    private static bool IsAutoClaimed(ActiveActivityTask task) =>
        task.Activity.Row.ActivityType == (int)PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeCatchTask;

    private async Task<ActiveActivityTask[]> FindRoleTasksAsync(LMNRoleInfo role, DateTimeOffset now)
    {
        var openEeveeChallenges = schedule.FindActive(now)
            .Where(activity => activity.Row.ActivityType == (int)PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeEeveeChallenge)
            .ToDictionary(activity => activity.Row.ActivityID);
        var drawnEeveeTasks = openEeveeChallenges.Count == 0
            ? []
            : (await eeveeChallenges.FindAsync(role.GameUlongUID))
                .Where(period => period.EeveeID != EeveeChallenge.NoEevee && openEeveeChallenges.ContainsKey(period.ActivityID))
                .SelectMany(period => FindDrawnEeveeTasks(openEeveeChallenges[period.ActivityID], period));
        return [.. schedule.FindActiveTasks(now).Concat(drawnEeveeTasks).DistinctBy(task => task.Task.TaskID)];
    }

    private IEnumerable<ActiveActivityTask> FindDrawnEeveeTasks(ScheduledActivity activity, EeveeChallengePeriod period)
    {
        var unlockedAt = DateTimeOffset.FromUnixTimeSeconds(period.DrawTime);
        return (eeveePool.Find(period.ActivityID, period.EeveeID)?.TaskIDList ?? [])
            .Select(taskID => schedule.TaskBase.Find(taskID))
            .OfType<pbTask_Base>()
            .Select(task => new ActiveActivityTask(activity, task, unlockedAt));
    }

    private static TaskProcessIncrement? FindBattleIncrement(ActiveActivityTask task, BattleTaskEvent battle, DateTimeOffset now)
    {
        var conditions = task.Task.FinishCondition.Where(condition => condition.ID != NoCondition).ToArray();
        return conditions is [var condition] && BattleTasks.MeasureBattle(condition, battle) is { } delta
            ? new TaskProcessIncrement(task.Task.TaskID, FindPeriodStart(task, now).ToUnixTimeSeconds(), delta, condition.TargetProcess)
            : null;
    }

    public async Task<ActivityTaskClaim> ClaimAsync(LMNRoleInfo role, int taskId)
    {
        var now = clock.GetUtcNow();
        var task = (await FindRoleTasksAsync(role, now)).FirstOrDefault(activeTask => activeTask.Task.TaskID == taskId);
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
            ? await BuildClaimedAsync(role, [claim.State!.ToTaskInfo()], [claim.Grant!], now)
            : ActivityTaskClaim.Refused(claim.Result);
    }

    public async Task<ActivityTaskClaim> ClaimGroupAsync(LMNRoleInfo role, int groupId)
    {
        var now = clock.GetUtcNow();
        var tasks = (await FindRoleTasksAsync(role, now))
            .Where(task => task.Task.GroupID == groupId && HasConditions(task))
            .ToArray();
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

        return claimed.Count > 0 ? await BuildClaimedAsync(role, [.. claimed], grants, now) : ActivityTaskClaim.Refused(lastRefusal);
    }

    private async Task<ActivityTaskClaim> BuildClaimedAsync(
        LMNRoleInfo role,
        pbCOMDT_TASK_INFO[] claimed,
        IReadOnlyList<ActivityTaskGrantResult> grants,
        DateTimeOffset now)
    {
        var chain = await ClaimAddResIdChainAsync(role, grants.SelectMany(grant => grant.Grant.AddedResources.Keys), now);
        var completeCount = (await BuildTaskInfoListAsync(role))
            .Count(task => task.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);
        return new ActivityTaskClaim(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            claimed,
            completeCount,
            MergeRewards(grants),
            chain.RewardedTasks,
            MergeRewards(chain.Grants),
            chain.ProgressedTasks);
    }

    private async Task<ActivityTaskChain> ClaimAddResIdChainAsync(LMNRoleInfo role, IEnumerable<int> addedResIds, DateTimeOffset now)
    {
        var rewarded = new List<pbCOMDT_TASK_INFO>();
        var grants = new List<ActivityTaskGrantResult>();
        var progressed = new Dictionary<int, ActiveActivityTask>();
        var pending = addedResIds.ToHashSet();
        for (var round = 0; round < MaxChainRounds && pending.Count > 0; round++)
        {
            var tasks = FindAddResIdTasks(pending, now);
            foreach (var task in tasks)
            {
                progressed.TryAdd(task.Task.TaskID, task);
            }

            var progress = await LoadProgressAsync(role, tasks, now);
            pending = [];
            foreach (var state in tasks.Select(progress.Evaluate).Where(state => state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete))
            {
                var claim = await ClaimEvaluatedAsync(role, state, now);
                if (claim.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
                {
                    continue;
                }

                rewarded.Add(claim.State!.ToTaskInfo());
                grants.Add(claim.Grant!);
                pending.UnionWith(claim.Grant!.Grant.AddedResources.Keys);
            }
        }

        var progressedTasks = progressed.Values.OrderBy(task => task.Task.TaskID).ToArray();
        var finalProgress = await LoadProgressAsync(role, progressedTasks, now);
        return new ActivityTaskChain(
            [.. rewarded],
            grants,
            [.. progressedTasks.Select(task => finalProgress.Evaluate(task).ToTaskInfo())]);
    }

    private static ActivityRewardGroup? MergeRewards(IReadOnlyList<ActivityTaskGrantResult> grants)
    {
        if (grants.Count == 0)
        {
            return null;
        }

        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var (coinID, beforeCoinNum) in grants.SelectMany(grant => grant.Commit.SpecialCoinsBefore))
        {
            specialCoinsBefore.TryAdd(coinID, beforeCoinNum);
        }

        return new ActivityRewardGroup(
            grants[^1].Commit.Wallet!,
            grants.Any(grant => grant.Grant.Change.CoinGrants.Keys.Any(CoinID.WalletCoins.Contains)),
            specialCoinsBefore,
            [.. grants.SelectMany(grant => grant.Commit.ChangedItems)
                .GroupBy(item => (item.ItemType, item.ItemId))
                .Select(group => group.Last())],
            [.. grants.SelectMany(grant => grant.Grant.PokemonIds)],
            [.. grants.SelectMany(grant => grant.Grant.Awards)]);
    }

    private ActiveActivityTask[] FindAddResIdTasks(IReadOnlySet<int> resIds, DateTimeOffset now) =>
    [
        .. schedule.FindActiveTasks(now)
            .DistinctBy(task => task.Task.TaskID)
            .Where(task => FindAddResIdCondition(task) is { } condition && condition.ParamA.Any(resIds.Contains))
            .OrderBy(task => task.Task.TaskID),
    ];

    private static pbFinishCondition? FindAddResIdCondition(ActiveActivityTask task) =>
        task.Task.FinishCondition.Where(condition => condition.ID != NoCondition).ToArray() is [var condition]
        && condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdAddResId
            ? condition
            : null;

    private TaskProcessIncrement[] FindAddResIdIncrements(IReadOnlyDictionary<int, int> addedResources, DateTimeOffset now) =>
    [
        .. FindAddResIdTasks(addedResources.Keys.ToHashSet(), now).Select(task =>
        {
            var condition = FindAddResIdCondition(task)!;
            var delta = condition.ParamA.Distinct()
                .Aggregate(0, (total, resId) => BattleTasks.AddCounts(total, addedResources.GetValueOrDefault(resId)));
            return new TaskProcessIncrement(task.Task.TaskID, FindPeriodStart(task, now).ToUnixTimeSeconds(), delta, condition.TargetProcess);
        }),
    ];

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
        var commit = await repository.CommitTaskRewardAsync(uid, reward, new ActivityRewardChange(
            grant.Items,
            grant.PokemonIds,
            grant.Change,
            FindAddResIdIncrements(grant.AddedResources, now)));
        if (commit.Wallet is null)
        {
            logger.LogInformation("Activity task {TaskId} claim refused for uid {Uid}: {Result}", taskId, uid, commit.Result);
            return (commit.Result, null, null);
        }

        foreach (var item in grant.Items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }

        foreach (var pokemonId in grant.PokemonIds)
        {
            role.GrantPokemon(pokemonId, rewardTime, (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTaskaward);
        }

        logger.LogInformation(
            "Activity task {TaskId} claimed by uid {Uid}: {RewardCount} rewards granted, Pokemon [{PokemonIds}]",
            taskId, uid, drops.Length, string.Join(", ", grant.PokemonIds));
        var claimed = state with
        {
            Status = PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded,
            RewardTime = rewardTime,
            AutoClaimed = IsAutoClaimed(state.Source),
        };
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, claimed, new ActivityTaskGrantResult(grant, commit));
    }

    private ActivityTaskGrant? BuildGrant(LMNRoleInfo role, pbDrop_Static[] drops, long rewardTime)
    {
        var items = new List<RoleItemInfo>();
        var pokemonIds = new List<int>();
        var coins = new Dictionary<int, long>();
        var bagGrants = new List<BagGrant>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        var addedResources = new Dictionary<int, int>();
        foreach (var drop in drops)
        {
            if (drop.ResNum <= 0)
            {
                return null;
            }

            addedResources[drop.ResID] = BattleTasks.AddCounts(addedResources.GetValueOrDefault(drop.ResID), drop.ResNum);
            if (CoinID.IsGrantableCoin(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(BagManager.BuildCoinAward(drop.ResID, drop.ResNum));
            }
            else if (UniteLicense.IsUniteLicense(gameData, drop.ResID))
            {
                if (UniteLicense.PlanGrant(gameData, role, drop.ResID, drop.ResNum, pokemonIds) is not { } license)
                {
                    return null;
                }

                license.AddTo(pokemonIds, coins, awards);
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
                awards.Add(BagManager.BuildAward(drop.ResID, drop.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
            }
            else
            {
                return null;
            }
        }

        return new ActivityTaskGrant(items, pokemonIds, new BagChange { Grants = bagGrants, CoinGrants = coins }, awards, addedResources);
    }

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
        var storedProcesses = await taskProcesses.FindAsync(uid);
        return new ActivityTaskProgress(
            periods,
            loginDays,
            rewards,
            storedProcesses,
            RoleProfile.GetLevel(gameData, role),
            roleCreateTime);
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
        var activityPeriodStart = periodStart > task.Activity.ValidStart ? periodStart : task.Activity.ValidStart;
        return task.UnlockedAt is { } unlockedAt && unlockedAt > activityPeriodStart ? unlockedAt : activityPeriodStart;
    }

    private static bool HasConditions(ActiveActivityTask task) =>
        task.Task.FinishCondition.Any(condition => condition.ID != NoCondition);

    private sealed class ActivityTaskProgress(
        Dictionary<int, DateTimeOffset> periodStarts,
        DateOnly[] loginDays,
        ActivityTaskReward[] rewards,
        IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess> storedProcesses,
        int trainerLevel,
        long roleCreateTime)
    {
        public ActivityTaskState Evaluate(ActiveActivityTask task)
        {
            var periodStart = periodStarts[task.Task.TaskID];
            var firstCountedDay = DateOnly.FromDateTime(periodStart.UtcDateTime);
            var countedLoginDays = loginDays.Where(day => day >= firstCountedDay).Order().ToArray();
            var conditions = task.Task.FinishCondition.Where(condition => condition.ID != NoCondition).ToArray();
            var periodStartSeconds = periodStart.ToUnixTimeSeconds();
            var storedProcess = BattleTasks.FindStoredProcess(storedProcesses, task.Task.TaskID, periodStartSeconds);
            var process = conditions
                .Select(condition => Math.Min(MeasureProcess(condition, countedLoginDays.Length, storedProcess), condition.TargetProcess))
                .ToArray();
            var complete = conditions.All(IsMeasured)
                && conditions.Zip(process).All(pair => pair.Second >= pair.First.TargetProcess);
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
                : storedProcesses.TryGetValue((task.Task.TaskID, periodStartSeconds), out var stored)
                  && stored.CompleteTime != NotCompletedTime
                    ? Math.Max(recvTime, stored.CompleteTime)
                    : FindCompleteTime(conditions, countedLoginDays, recvTime);
            return new ActivityTaskState(
                task,
                periodStartSeconds,
                process,
                status,
                recvTime,
                completeTime,
                reward?.RewardTime ?? 0,
                reward is not null && IsAutoClaimed(task));
        }

        private int MeasureProcess(pbFinishCondition condition, int loginDayCount, int storedProcess) =>
            (PB_ENUM_RES_CONDITION_ID)condition.ID switch
            {
                PB_ENUM_RES_CONDITION_ID.PbResConditionIdEverydayLogin => loginDayCount,
                PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach => trainerLevel,
                PB_ENUM_RES_CONDITION_ID.PbResConditionIdAddResId => storedProcess,
                _ when BattleTasks.IsBattleCondition(condition) => storedProcess,
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
            || condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach
            || condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdAddResId
            || BattleTasks.IsBattleCondition(condition);

        private static bool IsEverydayLogin(pbFinishCondition condition) =>
            condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdEverydayLogin;
    }

    private sealed record ActivityTaskState(
        ActiveActivityTask Source,
        long PeriodStart,
        int[] Process,
        PB_ENUM_ENM_TASKSTATUS Status,
        long RecvTime,
        long CompleteTime,
        long RewardTime,
        bool AutoClaimed)
    {
        public pbTask_Base Task => Source.Task;

        public pbCOMDT_TASK_INFO ToTaskInfo() => AutoClaimed ? ToAutoClaimedTaskInfo() : new()
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

        private pbCOMDT_TASK_INFO ToAutoClaimedTaskInfo() => new()
        {
            TaskId = Task.TaskID,
            TaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemActivity,
            TaskProcess = { Process.Select(_ => UntrackedProcess) },
            Status = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved,
            RecvTime = RecvTime,
            CompleteTime = CompleteTime,
            CompleteNum = CompletedOnce,
        };
    }
}

public sealed record ActivityTaskReward(int TaskId, long PeriodStart, long RewardTime);

public sealed record ActivityBattleProgress(
    pbCOMDT_TASK_INFO[] UpdatedTasks,
    int CompleteCountBefore,
    int CompleteCountAfter,
    ActivityTaskClaim? AutoClaim = null);

public sealed record ActivityTaskGrant(
    IReadOnlyList<RoleItemInfo> Items,
    IReadOnlyList<int> PokemonIds,
    BagChange Change,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards,
    IReadOnlyDictionary<int, int> AddedResources);

public sealed record ActivityTaskGrantResult(ActivityTaskGrant Grant, ActivityRewardCommit Commit);

public sealed record ActivityTaskChain(
    pbCOMDT_TASK_INFO[] RewardedTasks,
    IReadOnlyList<ActivityTaskGrantResult> Grants,
    pbCOMDT_TASK_INFO[] ProgressedTasks);

public sealed record ActivityRewardGroup(
    RoleWallet Wallet,
    bool WalletCoinsChanged,
    IReadOnlyDictionary<int, long> SpecialCoinsBefore,
    IReadOnlyList<BagItem> ChangedItems,
    IReadOnlyList<int> PokemonIds,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards);

public sealed record ActivityTaskClaim(
    PB_ENUM_RES_ERROR_CODE Result,
    pbCOMDT_TASK_INFO[] UpdatedTasks,
    int CompleteTaskCount,
    ActivityRewardGroup? Rewards,
    pbCOMDT_TASK_INFO[] ChainedTasks,
    ActivityRewardGroup? ChainedRewards,
    pbCOMDT_TASK_INFO[] ProgressedTasks)
{
    public IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards =>
        ChainedRewards is { Awards.Count: > 0 } chained ? chained.Awards : Rewards?.Awards ?? [];

    public static ActivityTaskClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, [], 0, null, [], null, []);
}
