using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

public sealed record ActivitySettlementTask(pbTask_Base Task, long PeriodStart, bool AutoClaimed, pbDrop_Static[] Drops);

public sealed record ActivitySettlementPlan(SettlementTaskPlan Progress, ActivitySettlementTask[] Tasks, int CompleteCountBefore);

public sealed record ActivitySettlementReward(ActivityTaskGrantResult? Grant, pbCOMDT_TASK_INFO[] Tasks);

public sealed partial class ActivityManager
{
    public async Task<ActivitySettlementPlan> PrepareSettlementAsync(LMNRoleInfo role, BattleTaskEvent battle, DateTimeOffset now)
    {
        var tasks = (await FindRoleTasksAsync(role, now)).Where(HasConditions)
            .DistinctBy(task => task.Task.TaskID).OrderBy(task => task.Task.TaskID).ToArray();
        var before = await LoadProgressAsync(role, tasks, now);
        var templates = tasks.Select(task =>
        {
            var state = before.Evaluate(task);
            var conditions = task.Task.FinishCondition.Select((condition, index) => (Condition: condition, Index: index))
                .Where(pair => pair.Condition.ID != NoCondition).ToArray();
            var info = (state with { AutoClaimed = false }).ToTaskInfo();
            var indexes = conditions.Select(pair => state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved
                && IsSettlementStoredCondition(pair.Condition) ? pair.Index : -1).ToArray();
            if (conditions.Any(pair => !IsSettlementStoredCondition(pair.Condition)
                && pair.Condition.ID != (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdEverydayLogin
                && pair.Condition.ID != (int)PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach
                && !IsBindingAccount(pair.Condition)))
            {
                info.IsLockProcess = 1;
            }

            return new SettlementTaskTemplate(info, state.PeriodStart, conditions.Select(pair => pair.Condition.TargetProcess).ToArray(), indexes, SettlementTaskCompleteTime.StoredAfterReceived);
        }).ToArray();
        var progress = new SettlementTaskPlan("tasks.activity", now.ToUnixTimeSeconds(),
            tasks.SelectMany(task => FindBattleIncrement(task, battle, now)).ToArray(), templates);
        return new ActivitySettlementPlan(progress, tasks.Select(task => new ActivitySettlementTask(
            task.Task.Clone(), FindPeriodStart(task, now).ToUnixTimeSeconds(), IsAutoClaimed(task),
            schedule.FindDrops(task.Task.SDropID).Select(drop => drop.Clone()).ToArray())).ToArray(),
            templates.Count(task => task.Info.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete));
    }

    public static bool StoresSettlementProgress(ActivitySettlementPlan plan) => plan.Progress.Increments.Length > 0;

    public async Task<ActivityBattleProgress> ClaimSettlementRewardsAsync(LMNRoleInfo role, SettlementKey key, ActivitySettlementPlan plan, pbCOMDT_TASK_INFO[] infos)
    {
        var storedRewards = await repository.FindSettlementRewardsAsync(key);
        var afterCount = infos.Count(info => info.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);
        var initialCompleted = infos.Where(info => info.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete)
            .Select(info => info.TaskId).ToHashSet();
        var claimed = new List<pbCOMDT_TASK_INFO>();
        var grants = new List<ActivityTaskGrantResult>();
        var chained = new List<pbCOMDT_TASK_INFO>();
        var chainGrants = new List<ActivityTaskGrantResult>();
        var progressed = new HashSet<int>();
        var settledIds = new HashSet<int>();
        var pending = new HashSet<int>();
        foreach (var task in plan.Tasks.Where(task => task.AutoClaimed && initialCompleted.Contains(task.Task.TaskID)))
        {
            if (await ClaimAsync(task) is { } result)
            {
                claimed.Add(DisplayTask(infos.First(info => info.TaskId == task.Task.TaskID), task));
                grants.Add(result);
                pending.UnionWith(result.Grant.AddedResources.Keys);
            }
        }

        for (var round = 0; round < MaxChainRounds && pending.Count > 0; round++)
        {
            var candidates = plan.Tasks.Where(task => task.Task.FinishCondition.Where(condition => condition.ID != NoCondition).ToArray()
                is [var condition] && condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdAddResId
                && condition.ParamA.Any(pending.Contains)).ToArray();
            pending = [];
            foreach (var task in candidates)
            {
                progressed.Add(task.Task.TaskID);
                if (settledIds.Contains(task.Task.TaskID))
                {
                    continue;
                }

                if (await ClaimAsync(task) is { } result)
                {
                    chained.Add(DisplayTask(infos.First(info => info.TaskId == task.Task.TaskID), task));
                    chainGrants.Add(result);
                    pending.UnionWith(result.Grant.AddedResources.Keys);
                }
            }
        }

        var touched = plan.Progress.Increments.Select(increment => increment.TaskId).ToHashSet();
        var all = infos.Select(info => DisplayTask(info, plan.Tasks.First(task => task.Task.TaskID == info.TaskId))).ToArray();
        var auto = claimed.Count == 0 ? null : new ActivityTaskClaim(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            claimed.ToArray(), all.Count(info => info.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete),
            MergeRewards(grants), chained.ToArray(), MergeRewards(chainGrants), all.Where(info => progressed.Contains(info.TaskId)).ToArray());
        return new ActivityBattleProgress(all.Where(info => touched.Contains(info.TaskId)).ToArray(), plan.CompleteCountBefore, afterCount, auto);

        async Task<ActivityTaskGrantResult?> ClaimAsync(ActivitySettlementTask task)
        {
            var existing = storedRewards.GetValueOrDefault((task.Task.TaskID, task.PeriodStart));
            if (existing is null)
            {
                var info = infos.First(info => info.TaskId == task.Task.TaskID);
                if (info.Status != (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete)
                {
                    return null;
                }

                var grant = BuildGrant(role, task.Drops, plan.Progress.Now)
                    ?? throw new InvalidDataException($"Activity settlement task {task.Task.TaskID} has no reward ownership path");
                var increments = plan.Tasks.SelectMany(target => target.Task.FinishCondition
                    .Select((condition, index) => (Condition: condition, Index: index))
                    .Where(pair => pair.Condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdAddResId
                        && pair.Condition.ParamA.Any(grant.AddedResources.ContainsKey))
                    .Select(pair => new TaskProcessIncrement(target.Task.TaskID, target.PeriodStart,
                        pair.Condition.ParamA.Distinct().Aggregate(0, (total, resource) => BattleTasks.AddCounts(total, grant.AddedResources.GetValueOrDefault(resource))),
                        pair.Condition.TargetProcess, pair.Index))).ToArray();
                existing = await repository.CommitSettlementRewardAsync(key,
                    new ActivityTaskReward(task.Task.TaskID, task.PeriodStart, plan.Progress.Now), grant,
                    new ActivityRewardChange(grant.Items, grant.PokemonIds, grant.Change, increments), plan.Progress);
                storedRewards[(task.Task.TaskID, task.PeriodStart)] = existing;
            }

            settledIds.Add(task.Task.TaskID);
            infos = existing.Tasks;
            if (existing.Grant is { } result)
            {
                GrantToRole(role, result.Grant, plan.Progress.Now, PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTaskaward);
            }
            return existing.Grant;
        }
    }

    private static bool IsSettlementStoredCondition(pbFinishCondition condition) =>
        BattleTasks.IsActivityBattleCondition(condition) || ActivityClientActions.IsClientAction(condition)
        || condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdAddResId
        || condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdUseSpecPokemonWin;

    private static pbCOMDT_TASK_INFO DisplayTask(pbCOMDT_TASK_INFO source, ActivitySettlementTask task)
    {
        var info = source.Clone();
        info.IsLockProcess = 0;
        if (task.AutoClaimed && info.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded)
        {
            info.Status = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved;
            for (var index = 0; index < info.TaskProcess.Count; index++)
            {
                info.TaskProcess[index] = 0;
            }
            info.RewardTime = 0;
            info.CompleteNum = CompletedOnce;
        }
        return info;
    }
}
