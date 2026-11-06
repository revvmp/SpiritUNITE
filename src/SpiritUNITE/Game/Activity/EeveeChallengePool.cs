using SpiritUNITE.Databin;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed record EeveeChallengeEevee(
    int ActivityID,
    int EeveeID,
    pbOutSideItem_Base PokemonExpireTimeItem,
    long TimeLimited,
    IReadOnlyList<int> TaskIDList);

public sealed class EeveeChallengePool
{
    private const int ExpirePokemonParamIndex = 0;
    private const int ExpireDateParamIndex = 1;
    private const int ExpireTimeParamIndex = 2;
    private const long ConfigDateShift = 1_000_000;
    private const int NoCondition = 0;

    private readonly Dictionary<int, EeveeChallengeEevee[]> eeveesByActivity;

    public EeveeChallengePool(GameData gameData)
    {
        var tasksByGroup = gameData.TaskBase.Rows.ToLookup(task => task.GroupID);
        var expireTimeItems = gameData.OutSideItemBase.Rows
            .Where(item => item.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePokemonexpiretime
                && item.ItemParam.Count > ExpireTimeParamIndex
                && item.ItemParam[ExpirePokemonParamIndex] > 0)
            .OrderBy(item => item.OutSideItemId)
            .ToArray();
        eeveesByActivity = gameData.ActivityBase.Rows
            .Where(activity => activity.ActivityType == (int)PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeEeveeChallenge)
            .ToDictionary(activity => activity.ActivityID, activity => BuildEevees(activity, expireTimeItems, tasksByGroup));
    }

    public IReadOnlyList<EeveeChallengeEevee> Find(int activityID) => eeveesByActivity.GetValueOrDefault(activityID) ?? [];

    public EeveeChallengeEevee? Find(int activityID, int eeveeID) =>
        Find(activityID).FirstOrDefault(eevee => eevee.EeveeID == eeveeID);

    private static EeveeChallengeEevee[] BuildEevees(
        pbActivity_Base activity,
        pbOutSideItem_Base[] expireTimeItems,
        ILookup<int, pbTask_Base> tasksByGroup)
    {
        var items = expireTimeItems.Where(item => ExpireConfigTimeOf(item) == activity.ValidEndTime).ToArray();
        if (items.Length == 0)
        {
            return [];
        }

        var tasks = tasksByGroup[activity.ActivityParam].OrderBy(task => task.TaskID).ToArray();
        var tasksPerEevee = tasks.Length % items.Length == 0 ? tasks.Length / items.Length : 0;
        return
        [
            .. items.Select((item, index) =>
            {
                var eeveeID = item.ItemParam[ExpirePokemonParamIndex];
                var block = tasks.Skip(index * tasksPerEevee).Take(tasksPerEevee).ToArray();
                var blockNamesEevee = block.Any(task => task.FinishCondition
                    .Any(condition => condition.ID != NoCondition && condition.ParamA.Contains(eeveeID)));
                return new EeveeChallengeEevee(
                    activity.ActivityID,
                    eeveeID,
                    item,
                    ActivitySchedule.ParseConfigTime(ExpireConfigTimeOf(item)).ToUnixTimeSeconds(),
                    blockNamesEevee ? [.. block.Select(task => task.TaskID)] : []);
            }),
        ];
    }

    private static long ExpireConfigTimeOf(pbOutSideItem_Base item) =>
        item.ItemParam[ExpireDateParamIndex] * ConfigDateShift + item.ItemParam[ExpireTimeParamIndex];
}
