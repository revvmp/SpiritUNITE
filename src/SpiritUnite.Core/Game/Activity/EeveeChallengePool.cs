using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

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
    private const int NoTaskID = 0;

    private readonly Dictionary<int, EeveeChallengeEevee[]> eeveesByActivity;

    public EeveeChallengePool(GameData gameData)
    {
        var expireTimeItems = gameData.OutSideItemBase.Rows
            .Where(item => item.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypePokemonexpiretime
                && item.ItemParam.Count > ExpireTimeParamIndex
                && item.ItemParam[ExpirePokemonParamIndex] > 0)
            .OrderBy(item => item.OutSideItemId)
            .ToArray();
        var challengesByActivity = gameData.EeveeChallenge.Rows.ToLookup(challenge => challenge.ActivityID);
        eeveesByActivity = gameData.ActivityBase.Rows
            .Where(activity => activity.ActivityType == (int)PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeEeveeChallenge)
            .ToDictionary(activity => activity.ActivityID, activity => BuildEevees(activity, challengesByActivity[activity.ActivityID], expireTimeItems));
    }

    public IReadOnlyList<EeveeChallengeEevee> Find(int activityID) => eeveesByActivity.GetValueOrDefault(activityID) ?? [];

    public EeveeChallengeEevee? Find(int activityID, int eeveeID) =>
        Find(activityID).FirstOrDefault(eevee => eevee.EeveeID == eeveeID);

    private static EeveeChallengeEevee[] BuildEevees(
        pbActivity_Base activity,
        IEnumerable<pbEeveeChallenge> challenges,
        pbOutSideItem_Base[] expireTimeItems) =>
        [
            .. challenges
                .Select(challenge => (Challenge: challenge, Item: expireTimeItems.FirstOrDefault(item =>
                    item.ItemParam[ExpirePokemonParamIndex] == challenge.EeveeID && ExpireConfigTimeOf(item) == activity.ValidEndTime)))
                .Where(pair => pair.Item is not null)
                .Select(pair => new EeveeChallengeEevee(
                    activity.ActivityID,
                    pair.Challenge.EeveeID,
                    pair.Item!,
                    ActivitySchedule.ParseConfigTime(ExpireConfigTimeOf(pair.Item!)).ToUnixTimeSeconds(),
                    [.. pair.Challenge.TaskIDList.Where(taskID => taskID != NoTaskID)])),
        ];

    private static long ExpireConfigTimeOf(pbOutSideItem_Base item) =>
        item.ItemParam[ExpireDateParamIndex] * ConfigDateShift + item.ItemParam[ExpireTimeParamIndex];
}
