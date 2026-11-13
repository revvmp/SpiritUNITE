using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Achievement;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.BattlePass;
using SpiritUnite.Game.Newbie;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Tasks;

public sealed class TaskUpdates(ActivityManager activityManager, AchievementManager achievementManager, BattlePassTasks battlePassTasks, TaskProcessRepository taskProcesses)
{
    private static readonly IReadOnlySet<PB_ENUM_RES_CONDITION_ID> OwnershipConditions = new HashSet<PB_ENUM_RES_CONDITION_ID>
    {
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdOwnSuitResContestantCardDecoration,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdOwnResAvatarAny,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdOwnResPokemon,
    };

    private static readonly IReadOnlySet<PB_ENUM_RES_CONDITION_ID> TrainerLevelConditions = new HashSet<PB_ENUM_RES_CONDITION_ID>
    {
        PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach,
    };

    public async Task<pbSCUpdateTaskInfoNtf> BuildBadgeGroupUpdateTaskInfoNtfAsync(LMNRoleInfo role, int grantedBadgeQuality) =>
        ToUpdateTaskInfoNtf(await achievementManager.BuildBadgeGroupTaskInfoListAsync(role, grantedBadgeQuality));

    public async Task<pbSCUpdateTaskInfoNtf> BuildOwnershipUpdateTaskInfoNtfAsync(LMNRoleInfo role) =>
        ToUpdateTaskInfoNtf(await achievementManager.BuildTaskInfoListAsync(role, OwnershipConditions));

    public async Task<pbSCUpdateTaskInfoNtf> BuildTrainerLevelUpdateTaskInfoNtfAsync(LMNRoleInfo role) =>
        ToUpdateTaskInfoNtf(
        [
            .. await activityManager.BuildTaskInfoListAsync(role, PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach),
            .. await achievementManager.BuildTaskInfoListAsync(role, TrainerLevelConditions),
        ]);

    public async Task<BattleTaskSettlementPlan> PrepareSettlementAsync(LMNRoleInfo role, BattleTaskEvent battle, DateTimeOffset now) => new(
        await activityManager.PrepareSettlementAsync(role, battle, now),
        await achievementManager.PrepareSettlementAsync(role, battle, now),
        await battlePassTasks.PrepareSettlementAsync(role, battle, now));

    public async Task<BattleTaskUpdate> ApplySettlementAsync(
        LMNRoleInfo role,
        SettlementKey key,
        BattleTaskSettlementPlan plan,
        NewbieChallengeSettlementPlan newbiePlan,
        SettlementTaskPlan? guildTasks,
        SettlementSharedEffects sharedEffects,
        Func<pbCOMDT_TASK_INFO[], IReadOnlyCollection<pbCOMDT_TASK_INFO>> otherBattleTasks)
    {
        var storesActivity = ActivityManager.StoresSettlementProgress(plan.Activity);
        List<SettlementTaskPlan> storedPlans = [newbiePlan.Tasks, plan.Achievements, plan.BattlePass];
        if (guildTasks is not null)
        {
            storedPlans.Add(guildTasks);
        }

        if (storesActivity)
        {
            storedPlans.Add(plan.Activity.Progress);
        }

        var newbieReceive = newbiePlan.ReceiveChallenge ? new NewbieChallengeSettlementReceive(newbiePlan.TaskDays, newbiePlan.Tasks.Now) : null;
        var stored = await taskProcesses.ApplySettlementsAsync(key, storedPlans, newbieReceive, sharedEffects);
        var (newbieProgress, achievements, battlePass) = (stored[0], stored[1], stored[2]);
        var activity = storesActivity
            ? await activityManager.ClaimSettlementRewardsAsync(role, key, plan.Activity, stored[^1])
            : new ActivityBattleProgress([], plan.Activity.CompleteCountBefore, plan.Activity.CompleteCountBefore);
        return new BattleTaskUpdate(
            [.. activity.UpdatedTasks, .. achievements, .. battlePass, .. otherBattleTasks(newbieProgress)],
            activity.CompleteCountBefore,
            activity.CompleteCountAfter,
            activity.AutoClaim);
    }

    public async Task<BattleTaskUpdate> RecordBattleAsync(
        LMNRoleInfo role,
        BattleTaskEvent battle,
        IReadOnlyCollection<pbCOMDT_TASK_INFO> otherBattleTasks)
    {
        var activity = await activityManager.RecordBattleAsync(role, battle);
        var achievements = await achievementManager.RecordBattleAsync(role, battle);
        var battlePass = await battlePassTasks.RecordBattleAsync(role, battle);
        return new BattleTaskUpdate(
            [.. activity.UpdatedTasks, .. achievements, .. battlePass, .. otherBattleTasks],
            activity.CompleteCountBefore,
            activity.CompleteCountAfter,
            activity.AutoClaim);
    }

    public static GamePacket BuildUpdateTaskInfoNtf(IEnumerable<pbCOMDT_TASK_INFO> tasks) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, ToUpdateTaskInfoNtf(tasks));

    private static pbSCUpdateTaskInfoNtf ToUpdateTaskInfoNtf(IEnumerable<pbCOMDT_TASK_INFO> tasks) => new()
    {
        TaskInfoList = { tasks.OrderBy(task => task.TaskId) },
    };
}

public sealed record BattleTaskUpdate(
    pbCOMDT_TASK_INFO[] UpdatedTasks,
    int ActivityCompleteCountBefore,
    int ActivityCompleteCountAfter,
    ActivityTaskClaim? AutoClaim)
{
    public GamePacket[] BuildProgressRedDots() => UpdatedTasks.Length == 0
        ? []
        :
        [
            .. TaskRedDots.BuildBattleProgressRedDots(ActivityCompleteCountBefore, ActivityCompleteCountAfter),
            .. AutoClaim is { } claim ? new[] { TaskRedDots.BuildCount(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdActivityReward, claim.CompleteTaskCount) } : [],
        ];
}

public sealed record BattleTaskSettlementPlan(ActivitySettlementPlan Activity, SettlementTaskPlan Achievements, SettlementTaskPlan BattlePass);
