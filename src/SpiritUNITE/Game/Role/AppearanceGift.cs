using Google.Protobuf;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Badge;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public sealed class AppearanceGift(
    BagManager bag,
    BagRepository bagRepository,
    BadgeManager badges,
    TaskUpdates taskUpdates,
    RoleInfoStore roles)
{
    private const int SingleBadge = 1;
    private const int SingleOutSideItem = 1;
    private const int UnlimitedBadgeStack = int.MaxValue;
    private const int NoItemLevel = 0;

    private static readonly int[] GiftAvatarIDs = [1004001103, 1004003801];
    private static readonly int[] GiftBadgeIDs = [1007100191, 1007100211, 1007100841, 1007100161, 1007100831];
    private const int GiftOutSideItemID = 1002000054;

    public async Task<IReadOnlyList<(PB_ENUM_CS_MSGID_DEF MsgID, IMessage Body)>> GrantAsync(LMNRoleInfo role, long gainTime)
    {
        var pushes = new List<(PB_ENUM_CS_MSGID_DEF MsgID, IMessage Body)>();
        foreach (var avatarID in GiftAvatarIDs)
        {
            if (role.GrantItem(RoleItemType.AvatarItem, avatarID, gainTime) is not { } avatar)
            {
                continue;
            }

            await roles.SaveAsync(role);
            pushes.Add((PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf, new pbSCUpdateAvatarNtf
            {
                UpdateType = (int)PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeGain,
                AvatarInfo = avatar.ToAvatarInfo(),
            }));
            pushes.Add(LobbyNotices.BuildRedDotCountNtf(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdAddAvatar, CountNewTagAvatars(role)));
            pushes.Add(await BuildOwnershipTasksAsync(role));
        }

        foreach (var badgeID in GiftBadgeIDs)
        {
            var grant = new BagGrant(RoleItemType.Badge, badgeID, SingleBadge, UnlimitedBadgeStack, NoItemLevel, BadgeManager.ShowNewTag);
            var commit = await bagRepository.CommitAsync(role.GameUlongUID, new BagChange { Grants = [grant] }, gainTime);
            if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                continue;
            }

            pushes.Add((PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildBadgeGroupUpdateTaskInfoNtfAsync(role)));
            pushes.Add((PB_ENUM_CS_MSGID_DEF.PbScidUpdateBadgeNotify, BadgeManager.BuildUpdateBadgeNotify(commit.ChangedItems)));
            pushes.Add((PB_ENUM_CS_MSGID_DEF.PbScidGetBadgePictureNtf, await BuildGainedBadgePictureNtfAsync(role)));
            pushes.Add(await BuildOwnershipTasksAsync(role));
        }

        if (bag.CreateOutSideItemGrant(GiftOutSideItemID, SingleOutSideItem) is { } itemGrant)
        {
            var commit = await bagRepository.CommitAsync(role.GameUlongUID, new BagChange { Grants = [itemGrant] }, gainTime);
            if (commit.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
            {
                pushes.Add((PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems)));
                pushes.Add(await BuildOwnershipTasksAsync(role));
            }
        }

        return pushes;
    }

    private static int CountNewTagAvatars(LMNRoleInfo role) =>
        role.CountNewTags(RoleItemType.AvatarItem);

    private async Task<(PB_ENUM_CS_MSGID_DEF MsgID, IMessage Body)> BuildOwnershipTasksAsync(LMNRoleInfo role) =>
        (PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role));

    private async Task<pbSCGetBadgePictureNtf> BuildGainedBadgePictureNtfAsync(LMNRoleInfo role)
    {
        var picture = await badges.BuildGetBadgePictureRspAsync(role.GameUlongUID);
        return new pbSCGetBadgePictureNtf
        {
            ResultInfo = new pbResultInfo(),
            BadgePictureInfo = picture.BadgePictureInfo,
            NewBadgePictureInfo = picture.BadgePictureInfo.Clone(),
        };
    }
}
