using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Game.Guild;

public sealed record GuildBattle(
    bool Won,
    bool Mvp,
    int KillCnt,
    int AssistCnt,
    int ScoreCnt,
    int Score,
    int UltimateSkillCnt,
    IReadOnlyList<ulong> TeammateUids);

public sealed record GuildActivityTaskClaim(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_TASK_INFO? TaskInfo, ActivityRewardGroup? Rewards)
{
    public static GuildActivityTaskClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, null);
}

public sealed class GuildActivityTasks(
    GuildRepository repository,
    GuildActivityManager activities,
    TaskProcessRepository taskProcesses,
    GameData gameData,
    BagManager bag,
    TimeProvider clock,
    ILogger<GuildActivityTasks> logger)
{
    private const int OneBattle = 1;
    private const int NotCompleted = 0;
    private const int CompletedOnce = 1;
    private const int NoCompleteTime = 0;

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(ulong uid) =>
        await FindTasksAsync(uid) is { } found
            ? [.. (await LoadStatesAsync(uid, found)).Select(state => state.ToTaskInfo())]
            : [];

    public async Task RecordBattleAsync(ulong uid, GuildBattle battle)
    {
        if (await FindTasksAsync(uid) is not { } found)
        {
            return;
        }

        var memberUids = (await repository.GetMembersAsync(found.Member.GuildID)).Select(member => member.GameUlongUID).ToHashSet();
        var withGuildMember = battle.TeammateUids.Any(teammate => teammate != uid && memberUids.Contains(teammate));
        var now = clock.GetUtcNow();
        var increments = found.Tasks
            .Select(task => (Task: task, Condition: task.FinishCondition.FirstOrDefault()))
            .Where(entry => entry.Condition is not null)
            .Select(entry => (entry.Task, entry.Condition, Delta: Measure(entry.Condition!, battle, withGuildMember)))
            .Where(entry => entry.Delta > 0)
            .Select(entry => new TaskProcessIncrement(entry.Task.TaskID, PeriodStartOf(entry.Task, found.Period, now), entry.Delta, entry.Condition!.TargetProcess))
            .ToArray();
        if (increments.Length == 0)
        {
            return;
        }

        if (!await taskProcesses.AddAsync(uid, increments, now.ToUnixTimeSeconds()))
        {
            logger.LogWarning("Guild activity task progress of uid {Uid} was not saved", uid);
            return;
        }

        logger.LogInformation("Uid {Uid} progressed guild activity tasks [{TaskIds}] in guild {GuildID}",
            uid, string.Join(", ", increments.Select(increment => increment.TaskId)), found.Member.GuildID);
    }

    public async Task<GuildActivityTaskClaim> ClaimAsync(LMNRoleInfo role, int taskId)
    {
        var uid = role.GameUlongUID;
        if (await repository.FindMemberAsync(uid) is null)
        {
            return GuildActivityTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist);
        }

        if (await FindTasksAsync(uid) is not { } found
            || (await LoadStatesAsync(uid, found)).FirstOrDefault(candidate => candidate.Task.TaskID == taskId) is not { } state)
        {
            return GuildActivityTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist);
        }

        if (state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved)
        {
            return GuildActivityTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        if (state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded)
        {
            return GuildActivityTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        }

        var rewardTime = clock.GetUtcNow().ToUnixTimeSeconds();
        var drops = gameData.DropStatic.Rows
            .Where(drop => drop.SDropID == state.Task.SDropID)
            .OrderBy(drop => drop.SDropSeq)
            .ToArray();
        if (drops.Length == 0 || GuildRewardGrant.Plan(gameData, bag, role, drops, rewardTime) is not { } grant)
        {
            logger.LogWarning("Guild activity task {TaskId} drop {SDropID} has a reward with no grant path", taskId, state.Task.SDropID);
            return GuildActivityTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var commit = await repository.CommitActivityTaskRewardAsync(new RoleGuildActivityTask
        {
            GameUlongUID = uid,
            TaskId = taskId,
            PeriodStart = state.PeriodStart,
            GuildID = found.Member.GuildID,
            RewardTime = rewardTime,
        }, found.Period.PeriodID, MaxGuildScore(), grant);
        if (commit.Wallet is null)
        {
            return GuildActivityTaskClaim.Refused(commit.Result);
        }

        GuildActivityManager.GrantItemsInMemory(role, grant);
        logger.LogInformation("Uid {Uid} claimed guild activity task {TaskId}: +{GuildScore} score for guild {GuildID}",
            uid, taskId, grant.GuildScore, found.Member.GuildID);
        var claimed = state with { Status = PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded, RewardTime = rewardTime };
        return new GuildActivityTaskClaim(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            claimed.ToTaskInfo(),
            GuildRewardClaim.Granted(grant, commit).Rewards);
    }

    private int MaxGuildScore() => gameData.GuildActivityScoreGear.Rows.Select(gear => gear.Score).DefaultIfEmpty(0).Max();

    private async Task<GuildTaskSet?> FindTasksAsync(ulong uid)
    {
        if (await repository.FindMemberAsync(uid) is not { } member)
        {
            return null;
        }

        var period = activities.CurrentPeriod();
        if (await activities.FindStartedActivityAsync(member.GuildID, period) is not { } activity
            || gameData.GuildActivityTask.Find(activity.RewardID) is not { } taskRow)
        {
            return null;
        }

        var tasks = taskRow.TaskID
            .Distinct()
            .Select(taskId => gameData.TaskBase.Find(taskId))
            .OfType<pbTask_Base>()
            .ToArray();
        return new GuildTaskSet(member, period, tasks);
    }

    private async Task<GuildTaskState[]> LoadStatesAsync(ulong uid, GuildTaskSet found)
    {
        var now = clock.GetUtcNow();
        var periodStart = UTCTimeUtil.CfgTime2ServerUnixTime(found.Period.Row!.StartTime);
        var stored = await taskProcesses.FindAsync(uid);
        var rewards = (await repository.GetActivityTaskRewardsAsync(uid, periodStart))
            .ToDictionary(reward => (reward.TaskId, reward.PeriodStart), reward => reward.RewardTime);
        return
        [
            .. found.Tasks.Select(task =>
            {
                var taskPeriodStart = PeriodStartOf(task, found.Period, now);
                var target = task.FinishCondition.FirstOrDefault()?.TargetProcess ?? 0;
                var process = BattleTasks.FindStoredProcess(stored, task.TaskID, taskPeriodStart);
                var completeTime = stored.TryGetValue((task.TaskID, taskPeriodStart), out var storedProcess) ? storedProcess.CompleteTime : NoCompleteTime;
                var status = rewards.ContainsKey((task.TaskID, taskPeriodStart)) ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
                    : target > 0 && process >= target ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete
                    : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved;
                return new GuildTaskState(
                    task,
                    taskPeriodStart,
                    Math.Min(process, Math.Max(target, 0)),
                    status,
                    completeTime,
                    rewards.GetValueOrDefault((task.TaskID, taskPeriodStart)));
            }),
        ];
    }

    private static long PeriodStartOf(pbTask_Base task, GuildActivityPeriod period, DateTimeOffset now)
    {
        var periodStart = UTCTimeUtil.CfgTime2ServerUnixTime(period.Row!.StartTime);
        if ((PB_ENUM_RES_TASK_RESETTYPE)task.ResetType != PB_ENUM_RES_TASK_RESETTYPE.PbResTaskResettypeDaily)
        {
            return periodStart;
        }

        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        return Math.Max(today, periodStart);
    }

    private static int Measure(pbFinishCondition condition, GuildBattle battle, bool withGuildMember) =>
        (PB_ENUM_RES_CONDITION_ID)condition.ID switch
        {
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdPlayWithGuildMember => withGuildMember ? OneBattle : 0,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdPlayWithGuildMemberToWin => withGuildMember && battle.Won ? OneBattle : 0,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotoalMvpCnt => battle.Mvp ? OneBattle : 0,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotalScoreCnt => Math.Max(0, battle.ScoreCnt),
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotalScore => Math.Max(0, battle.Score),
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotalKillCnt => Math.Max(0, battle.KillCnt),
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdTotalAssistCnt => Math.Max(0, battle.AssistCnt),
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdSceneUltimateSkill => Math.Max(0, battle.UltimateSkillCnt),
            _ => 0,
        };

    private sealed record GuildTaskSet(GuildMember Member, GuildActivityPeriod Period, pbTask_Base[] Tasks);

    private sealed record GuildTaskState(
        pbTask_Base Task,
        long PeriodStart,
        int Process,
        PB_ENUM_ENM_TASKSTATUS Status,
        long CompleteTime,
        long RewardTime)
    {
        public pbCOMDT_TASK_INFO ToTaskInfo() => new()
        {
            TaskId = Task.TaskID,
            TaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemGuildActivity,
            CompleteNum = Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved ? NotCompleted : CompletedOnce,
            TaskProcess = { Process },
            Status = (int)Status,
            RecvTime = PeriodStart,
            RewardTime = RewardTime,
            CompleteTime = CompleteTime,
        };
    }
}
