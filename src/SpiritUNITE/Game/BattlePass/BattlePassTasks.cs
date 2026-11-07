using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.BattlePass;

public sealed record BattlePassTaskClaim(
    PB_ENUM_RES_ERROR_CODE Result,
    pbCOMDT_TASK_INFO[] UpdatedTasks,
    BattlePassCommit? Commit)
{
    public static BattlePassTaskClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, [], null);
}

public sealed record BattlePassTaskFinishCnt(int DailyTaskFinishCnt, int WeeklyTaskFinishCnt, int ChallengeTaskFinishCnt);

public sealed class BattlePassTasks(
    BattlePassSeasons seasons,
    BattlePassLeveling leveling,
    BattlePassRepository repository,
    TaskProcessRepository taskProcesses,
    ActivityRepository loginDays,
    TimeProvider clock,
    ILogger<BattlePassTasks> logger)
{
    private const int NoCondition = 0;
    private const int UntrackedProcess = 0;
    private const int NotCompleted = 0;
    private const int CompletedOnce = 1;
    private const int NotCompletedTime = 0;
    private const int WelfareTaskAddType = 1;
    private const int NormalTaskAddType = 0;
    private const int LockedProcess = 1;
    private const int DaysPerWeek = 7;
    private const int BpExpResID = (int)PB_ENUM_ENM_RESOURCES_ID.PbEnmResourcesIdBpExp;

    private readonly ILookup<int, pbDrop_Static> dropsById = seasons.GameData.DropStatic.Rows
        .OrderBy(drop => drop.SDropSeq)
        .ToLookup(drop => drop.SDropID);

    public static bool IsBattlePassModule(int moduleType) =>
        moduleType == (int)PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleBattlepass;

    public async Task<pbSCGetTaskInfoRsp> BuildTaskInfoRspAsync(LMNRoleInfo role)
    {
        var now = clock.GetUtcNow();
        var current = seasons.FindCurrentSeason(now);
        var response = new pbSCGetTaskInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            ModuleType = (int)PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleBattlepass,
        };
        if (!current.IsInProcess)
        {
            return response;
        }

        var states = await EvaluateAsync(role, current, now);
        response.TaskInfoList.Add(states.Select(state => state.ToTaskInfo()));
        if (seasons.FindTimeLimitTasks(current.Season.SeasonID, now).FirstOrDefault() is { } timeLimitTask)
        {
            response.TaskSysExtraInfo = new pbCOMDT_TASK_SYSTEM_EXTRA_INFO { BPTimeLimitTaskCfgID = timeLimitTask.ID };
        }

        return response;
    }

    public async Task<BattlePassTaskFinishCnt> CountFinishedTasksAsync(LMNRoleInfo role, BattlePassSeasonState season, DateTimeOffset now)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(now.ToUnixTimeSeconds(), season.StartTime, season.EndTime));
        var finished = (await EvaluateAsync(role, season, at))
            .Where(state => state.Status != PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved)
            .ToArray();
        int Count(PB_ENUM_ENM_TASKSYSTEM taskSystem) => finished.Count(state => state.Slot.TaskSystem == taskSystem);
        return new BattlePassTaskFinishCnt(
            Count(PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemBattlepassDailytask),
            Count(PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemBattlepassWeektask),
            Count(PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemBattlepassSeasontask));
    }

    public async Task<pbCOMDT_TASK_INFO[]> RecordBattleAsync(LMNRoleInfo role, BattleTaskEvent battle)
    {
        var now = clock.GetUtcNow();
        var current = seasons.FindCurrentSeason(now);
        if (!current.IsInProcess)
        {
            return [];
        }

        var states = await EvaluateAsync(role, current, now);
        var increments = states
            .Where(state => state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved && !state.Locked)
            .Select(state => FindBattleIncrement(state, battle))
            .OfType<TaskProcessIncrement>()
            .ToArray();
        if (increments.Length == 0)
        {
            return [];
        }

        if (!await taskProcesses.AddAsync(role.GameUlongUID, increments, now.ToUnixTimeSeconds()))
        {
            logger.LogWarning("Battle pass task progress of uid {Uid} was not stored: the role row is missing", role.GameUlongUID);
            return [];
        }

        var touched = increments.Select(increment => increment.TaskId).ToHashSet();
        return [.. (await EvaluateAsync(role, current, now)).Where(state => touched.Contains(state.Task.TaskID)).Select(state => state.ToTaskInfo())];
    }

    public async Task<BattlePassTaskClaim> ClaimAsync(LMNRoleInfo role, int taskId, bool isBatch)
    {
        var uid = role.GameUlongUID;
        var now = clock.GetUtcNow();
        var current = seasons.FindCurrentSeason(now);
        if (!current.IsInProcess)
        {
            logger.LogInformation("Battle pass task claim from uid {Uid} refused: no season in process", uid);
            return BattlePassTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrBattlePassInvalidSeasonState);
        }

        var states = await EvaluateAsync(role, current, now);
        var picked = isBatch
            ? states.Where(state => state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete).ToArray()
            : states.Where(state => state.Task.TaskID == taskId).Take(1).ToArray();
        if (picked.Length == 0)
        {
            logger.LogInformation("Battle pass task claim of task {TaskId} (batch {IsBatch}) from uid {Uid} refused: no such task", taskId, isBatch, uid);
            return BattlePassTaskClaim.Refused(isBatch ? PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked : PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist);
        }

        if (picked.FirstOrDefault(state => state.Status != PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete) is { } notComplete)
        {
            var result = notComplete.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
                ? PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked
                : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            logger.LogInformation("Battle pass task {TaskId} claim from uid {Uid} refused: {Result}", notComplete.Task.TaskID, uid, result);
            return BattlePassTaskClaim.Refused(result);
        }

        if (picked.Select(state => FindExp(state.Task)).ToArray() is var exps && exps.Any(exp => exp is null))
        {
            logger.LogWarning("Battle pass task claim from uid {Uid} refused: a task drop holds a reward other than battle pass exp", uid);
            return BattlePassTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var exp = exps.Sum(value => value!.Value);
        var rewardTime = now.ToUnixTimeSeconds();
        BattlePassTaskRewardRecord[] rewards = [.. picked.Select(state => new BattlePassTaskRewardRecord(state.Task.TaskID, state.PeriodStart, rewardTime))];
        var commit = await repository.CommitAsync(uid, current.Season.SeasonID, stored => new BattlePassDecision(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            new BattlePassMutation(leveling.GainExp(stored.Progress, exp, rewardTime), [], BattlePassGrant.Empty, rewards)), rewardTime);
        if (!commit.Succeeded)
        {
            logger.LogInformation("Battle pass task claim from uid {Uid} refused: {Result}", uid, commit.Result);
            return BattlePassTaskClaim.Refused(commit.Result);
        }

        var progress = commit.Stored!.Progress;
        logger.LogInformation(
            "Battle pass tasks [{TaskIds}] claimed by uid {Uid}: {Exp} exp, season {SeasonID} level {Level} exp {LevelExp}",
            string.Join(", ", rewards.Select(reward => reward.TaskId)), uid, exp, progress.SeasonID, progress.Level, progress.Exp);
        var claimedIds = rewards.Select(reward => reward.TaskId).ToHashSet();
        return new BattlePassTaskClaim(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            [.. (await EvaluateAsync(role, current, now)).Where(state => claimedIds.Contains(state.Task.TaskID)).Select(state => state.ToTaskInfo())],
            commit);
    }

    private int? FindExp(pbTask_Base task)
    {
        var drops = dropsById[task.SDropID].ToArray();
        return drops.Length == 0 || drops.Any(drop => drop.ResID != BpExpResID || drop.ResNum <= 0)
            ? null
            : drops.Sum(drop => drop.ResNum);
    }

    private static TaskProcessIncrement? FindBattleIncrement(BattlePassTaskState state, BattleTaskEvent battle) =>
        state.Conditions is [var condition] && BattleTasks.MeasureBattle(condition, battle) is { } delta
            ? new TaskProcessIncrement(state.Task.TaskID, state.PeriodStart, delta, condition.TargetProcess)
            : null;

    private async Task<BattlePassTaskState[]> EvaluateAsync(LMNRoleInfo role, BattlePassSeasonState current, DateTimeOffset now)
    {
        var uid = role.GameUlongUID;
        var progress = (await repository.LoadAsync(uid, current.Season.SeasonID)).Progress;
        var slots = seasons.FindTaskSlots(current, now)
            .DistinctBy(slot => slot.TaskID)
            .Select(slot => (Slot: slot, Task: seasons.GameData.TaskBase.Find(slot.TaskID)))
            .Where(pair => pair.Task is not null)
            .ToArray();
        var stored = await taskProcesses.FindAsync(uid);
        var rewards = (await repository.FindTaskRewardsAsync(uid, current.StartTime))
            .ToDictionary(reward => (reward.TaskId, reward.PeriodStart));
        var days = await loginDays.FindLoginDaysAsync(uid, DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(current.StartTime).UtcDateTime));
        var lastCountedDay = DateOnly.FromDateTime(now.UtcDateTime);
        return
        [
            .. slots.Select(pair =>
            {
                var task = pair.Task!;
                var periodStart = FindPeriodStart(task, current, now);
                var conditions = task.FinishCondition.Where(condition => condition.ID != NoCondition).ToArray();
                var firstCountedDay = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(periodStart).UtcDateTime);
                var loginDayCount = days.Count(day => day >= firstCountedDay && day <= lastCountedDay);
                var storedProcess = BattleTasks.FindStoredProcess(stored, task.TaskID, periodStart);
                int[] process = [.. conditions.Select(condition => Math.Min(MeasureProcess(condition, loginDayCount, storedProcess), condition.TargetProcess))];
                var locked = pair.Slot.IsWelfare && !progress.HasPass;
                var complete = !locked
                    && conditions.Length > 0
                    && conditions.All(IsMeasured)
                    && conditions.Zip(process).All(entry => entry.Second >= entry.First.TargetProcess);
                var reward = rewards.GetValueOrDefault((task.TaskID, periodStart));
                var status = reward is not null
                    ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
                    : complete ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved;
                var completeTime = status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved
                    ? NotCompletedTime
                    : stored.TryGetValue((task.TaskID, periodStart), out var storedTask) && storedTask.CompleteTime != NotCompletedTime
                        ? storedTask.CompleteTime
                        : periodStart;
                return new BattlePassTaskState(pair.Slot, task, conditions, periodStart, process, status, completeTime, reward?.RewardTime ?? 0, locked);
            }),
        ];
    }

    private static int MeasureProcess(pbFinishCondition condition, int loginDayCount, int storedProcess) =>
        (PB_ENUM_RES_CONDITION_ID)condition.ID switch
        {
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdEverydayLogin => loginDayCount,
            _ when BattleTasks.IsBattleCondition(condition) => storedProcess,
            _ => UntrackedProcess,
        };

    private static bool IsMeasured(pbFinishCondition condition) =>
        condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdEverydayLogin || BattleTasks.IsBattleCondition(condition);

    private static long FindPeriodStart(pbTask_Base task, BattlePassSeasonState current, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var periodStart = (PB_ENUM_RES_TASK_RESETTYPE)task.ResetType switch
        {
            PB_ENUM_RES_TASK_RESETTYPE.PbResTaskResettypeDaily => today.ToUnixTimeSeconds(),
            PB_ENUM_RES_TASK_RESETTYPE.PbResTaskResettypeWeek =>
                today.AddDays(-(((int)today.DayOfWeek + DaysPerWeek - (int)DayOfWeek.Monday) % DaysPerWeek)).ToUnixTimeSeconds(),
            _ => current.StartTime,
        };
        return Math.Max(periodStart, current.StartTime);
    }

    private sealed record BattlePassTaskState(
        BattlePassTaskSlot Slot,
        pbTask_Base Task,
        pbFinishCondition[] Conditions,
        long PeriodStart,
        int[] Process,
        PB_ENUM_ENM_TASKSTATUS Status,
        long CompleteTime,
        long RewardTime,
        bool Locked)
    {
        public pbCOMDT_TASK_INFO ToTaskInfo() => new()
        {
            TaskId = Task.TaskID,
            TaskSystem = (int)Slot.TaskSystem,
            CompleteNum = Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved ? NotCompleted : CompletedOnce,
            TaskProcess = { Process.Length > 0 ? Process : [UntrackedProcess] },
            Status = (int)Status,
            RecvTime = PeriodStart,
            IsLockProcess = Locked ? LockedProcess : 0,
            RewardTime = RewardTime,
            CompleteTime = CompleteTime,
            TaskAddType = Slot.IsWelfare ? WelfareTaskAddType : NormalTaskAddType,
        };
    }
}
