using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public static class RoleAvatar
{
    private const int MaxBatchClearAvatarNewTagCnt =
        (int)PB_MACRO_COM_MAX_BATCH_CLEAR_AVATAR_NEW_TAG_CNT.PbComMaxBatchClearAvatarNewTagCnt;

    private static readonly int AvatarSlotCount = Enum.GetValues<PB_ENUM_RES_AVATAR_SLOT_TYPE>()
        .Count(slot => slot is not (PB_ENUM_RES_AVATAR_SLOT_TYPE.PbResAvatarSlotTypeInvalid
            or PB_ENUM_RES_AVATAR_SLOT_TYPE.PbResAvatarSlotTypeMax));

    public static bool IsWearableList(IReadOnlyCollection<int> avatarIDList) =>
        avatarIDList.Count > 0 && avatarIDList.Count <= AvatarSlotCount && AreDistinctAvatarIDs(avatarIDList);

    public static bool IsNewTagClearList(IReadOnlyCollection<int> avatarIDList) =>
        avatarIDList.Count is > 0 and <= MaxBatchClearAvatarNewTagCnt && avatarIDList.All(IsAvatarID);

    private static bool AreDistinctAvatarIDs(IReadOnlyCollection<int> avatarIDList) =>
        avatarIDList.All(IsAvatarID) && avatarIDList.Distinct().Count() == avatarIDList.Count;

    private static bool IsAvatarID(int avatarID) => avatarID > 0;
}
