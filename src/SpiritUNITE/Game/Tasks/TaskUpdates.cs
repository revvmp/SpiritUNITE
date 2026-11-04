using SpiritUNITE.Game.Achievement;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Tasks;

public sealed class TaskUpdates(ActivityTasks activityTasks, AchievementTasks achievementTasks)
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

    private static readonly IReadOnlySet<PB_ENUM_RES_CONDITION_ID> BadgeGroupConditions = new HashSet<PB_ENUM_RES_CONDITION_ID>
    {
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdGetDiffGroupBadge,
    };

    public async Task<pbSCUpdateTaskInfoNtf> BuildBadgeGroupUpdateTaskInfoNtfAsync(LMNRoleInfo role) =>
        ToUpdateTaskInfoNtf(await achievementTasks.BuildTaskInfoListAsync(role, BadgeGroupConditions));

    public async Task<pbSCUpdateTaskInfoNtf> BuildOwnershipUpdateTaskInfoNtfAsync(LMNRoleInfo role) =>
        ToUpdateTaskInfoNtf(await achievementTasks.BuildTaskInfoListAsync(role, OwnershipConditions));

    public async Task<pbSCUpdateTaskInfoNtf> BuildTrainerLevelUpdateTaskInfoNtfAsync(LMNRoleInfo role) =>
        ToUpdateTaskInfoNtf(
        [
            .. await activityTasks.BuildTaskInfoListAsync(role, PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach),
            .. await achievementTasks.BuildTaskInfoListAsync(role, TrainerLevelConditions),
        ]);

    private static pbSCUpdateTaskInfoNtf ToUpdateTaskInfoNtf(IEnumerable<pbCOMDT_TASK_INFO> tasks) => new()
    {
        TaskInfoList = { tasks.OrderBy(task => task.TaskId) },
    };
}
