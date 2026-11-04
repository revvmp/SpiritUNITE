using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Badge;

public sealed record BadgeSchedue(int BadgeSchedueId, string SchedueName, IReadOnlyList<int> BadgeIDList, long UnlockTimeStamp)
{
    public const long Locked = 0;

    public static BadgeSchedue Empty(int badgeSchedueId) => new(badgeSchedueId, "", [], Locked);

    public pbCOMDT_BADGE_SCHEDUE_DATA BuildSchedueData() => new()
    {
        CellList = { BadgeIDList.Select(badgeID => new pbCOMDT_BADGE_CELL_NODE_INFO { BadgeID = badgeID }) },
    };
}
