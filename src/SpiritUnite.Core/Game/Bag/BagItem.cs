using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Bag;

public sealed record BagItem(RoleItemType ItemType, int ItemId, ulong GridUniqueID, int Cnt, int Level, long GainTime, int NewTag)
{
    public long UniqueID => (long)GridUniqueID;

    public bool IsOwned => Cnt > 0;

    public pbCOMDT_ONE_OUTSIDEITEM_GRID_INFO ToOutSideItemGridInfo() => IsOwned ? ToOwnedGridInfo() : ToEmptiedGridInfo();

    private pbCOMDT_ONE_OUTSIDEITEM_GRID_INFO ToEmptiedGridInfo() => new()
    {
        UniqueID = UniqueID,
        OutSideItemID = ItemId,
        OutSideItemExtra = new pbCOMDT_OUTSIDEITEM_EXTRA
        {
            OutSideItemUnion = new pbCOMDT_OUTSIDEITEM_UNION { HeldItemExperience = new pbCOMDT_HELDITEM_EXPERIENCE() },
        },
    };

    private pbCOMDT_ONE_OUTSIDEITEM_GRID_INFO ToOwnedGridInfo() => new()
    {
        UniqueID = UniqueID,
        OutSideItemID = ItemId,
        OutSideItemCnt = Cnt,
        OutSideItemLastAddTime = GainTime,
        OutSideItemLevel = Level,
        NewTag = NewTag,
        OutSideItemExtra = new pbCOMDT_OUTSIDEITEM_EXTRA { OutSideItemUnion = new pbCOMDT_OUTSIDEITEM_UNION() },
    };

    public pbCOMDT_ONE_BADGE_GRID_INFO ToBadgeGridInfo() => new()
    {
        UniqueID = UniqueID,
        BadgeID = ItemId,
        BadgeCnt = Cnt,
    };
}
