using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Role;

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

    public static PB_ENUM_RES_ERROR_CODE TakeOffSlot(
        GameData gameData,
        IReadOnlyCollection<int> wornAvatarIDList,
        int slotType,
        out int[] remainingAvatarIDList)
    {
        remainingAvatarIDList = [];
        if (!Enum.IsDefined((PB_ENUM_RES_AVATAR_SLOT_TYPE)slotType)
            || slotType is (int)PB_ENUM_RES_AVATAR_SLOT_TYPE.PbResAvatarSlotTypeInvalid or (int)PB_ENUM_RES_AVATAR_SLOT_TYPE.PbResAvatarSlotTypeMax)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        var slotAvatarIDs = wornAvatarIDList
            .Where(avatarID => gameData.AvatarItem.Find(avatarID)?.AvatarSlotType == slotType)
            .ToArray();
        if (slotAvatarIDs.Length == 0)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrAvatarUnexists;
        }

        if (slotAvatarIDs.Any(avatarID => CreateRoleShowAvatar.IsBodyFeature(gameData, avatarID)))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        }

        remainingAvatarIDList = [.. wornAvatarIDList.Except(slotAvatarIDs)];
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public static int[] ReplaceSlots(
        GameData gameData,
        IReadOnlyCollection<int> wornAvatarIDList,
        IReadOnlyCollection<int> newAvatarIDs)
    {
        var replacedSlots = newAvatarIDs
            .Select(avatarID => gameData.AvatarItem.Find(avatarID)?.AvatarSlotType)
            .OfType<int>()
            .ToHashSet();
        return
        [
            .. wornAvatarIDList
                .Where(avatarID => gameData.AvatarItem.Find(avatarID) is not { } avatarItem
                    || !replacedSlots.Contains(avatarItem.AvatarSlotType))
                .Concat(newAvatarIDs)
                .Distinct(),
        ];
    }

    private static bool AreDistinctAvatarIDs(IReadOnlyCollection<int> avatarIDList) =>
        avatarIDList.All(IsAvatarID) && avatarIDList.Distinct().Count() == avatarIDList.Count;

    private static bool IsAvatarID(int avatarID) => avatarID > 0;
}
