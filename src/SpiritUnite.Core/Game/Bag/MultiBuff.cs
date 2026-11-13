using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Bag;

public sealed record MultiBuff(
    PB_ENUM_RES_MULTIBUFF_TYPE MultiBuffType,
    PB_ENUM_RES_MULTIBUFF_SUBTYPE MultiBuffSubType,
    int MultipleValue,
    int LastUseMultiBuffResID,
    long TimeTypeBufEndTimeStamp)
{
    public bool IsActiveAt(long now) =>
        MultiBuffSubType == PB_ENUM_RES_MULTIBUFF_SUBTYPE.PbResMultibuffSubtypeTime && TimeTypeBufEndTimeStamp > now;

    public pbCOMDT_ONE_MULTIBUFF_INFO ToOneMultiBuffInfo() => new()
    {
        LastUseMultiBuffResID = LastUseMultiBuffResID,
        MultiBuffType = (int)MultiBuffType,
        MultipleValue = MultipleValue,
        MultiBuffSubType = (int)MultiBuffSubType,
        TimeTypeBufEndTimeStamp = TimeTypeBufEndTimeStamp,
    };
}

public sealed record MultiBuffCard(
    int OutSideItemId,
    PB_ENUM_RES_MULTIBUFF_TYPE MultiBuffType,
    int MultipleValue,
    PB_ENUM_RES_MULTIBUFF_SUBTYPE MultiBuffSubType,
    int Days)
{
    private const long SecondsPerDay = 86400;

    public MultiBuff ApplyTo(MultiBuff? current, int cardCnt, long now)
    {
        var extendsCurrent = current is not null
            && current.MultiBuffSubType == MultiBuffSubType
            && current.IsActiveAt(now);
        var start = extendsCurrent ? current!.TimeTypeBufEndTimeStamp : now;
        return new MultiBuff(MultiBuffType, MultiBuffSubType, MultipleValue, OutSideItemId, start + Days * SecondsPerDay * cardCnt);
    }
}

public sealed record MultiBuffUse(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<BagItem> ChangedItems, IReadOnlyList<MultiBuff> Buffs, long Now)
{
    public bool Succeeded => Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;

    public static MultiBuffUse Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, [], [], 0);
}
