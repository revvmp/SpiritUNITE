using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Bag;

public sealed record BagItem(RoleItemType ItemType, int ItemId, int Cnt, int Level, long GainTime, int NewTag)
{
    public long UniqueID => ItemId;

    public bool IsOwned => Cnt > 0;

    public pbCOMDT_ONE_OUTSIDEITEM_GRID_INFO ToOutSideItemGridInfo() => new()
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
