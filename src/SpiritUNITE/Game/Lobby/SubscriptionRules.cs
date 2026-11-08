using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Lobby;

public static class SubscriptionRules
{
    public const int DefaultBubbleID = 1007012001;

    public static IReadOnlyList<int> OwnedBubbleIDs { get; } = [DefaultBubbleID];

    public static PB_ENUM_RES_ERROR_CODE ValidateBubbleChange(int bubbleID) =>
        OwnedBubbleIDs.Contains(bubbleID) ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;

    public static PB_ENUM_RES_ERROR_CODE CollectCoupons() => PB_ENUM_RES_ERROR_CODE.PbErrSubscriptionOutdate;
}
