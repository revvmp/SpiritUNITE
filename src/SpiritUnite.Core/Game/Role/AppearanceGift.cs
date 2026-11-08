using Google.Protobuf;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Badge;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Role;

public sealed class AppearanceGift(
    GameData gameData,
    BagManager bag,
    BagRepository bagRepository,
    BadgeManager badges,
    TaskUpdates taskUpdates,
    RoleInfoStore roles)
{
    private const int UnlimitedBadgeStack = int.MaxValue;
    private const int NoItemLevel = 0;

    public async Task<IReadOnlyList<(PB_ENUM_CS_MSGID_DEF MsgID, IMessage Body)>> GrantAsync(LMNRoleInfo role, long gainTime)
    {
        var pushes = new List<(PB_ENUM_CS_MSGID_DEF MsgID, IMessage Body)>();
        foreach (var avatarID in NewPlayerDefaultGrants.ResourceIdsOf(gameData, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesAvatar))
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

        foreach (var badge in NewPlayerDefaultGrants.RowsOf(gameData, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesBadge))
        {
            var grant = new BagGrant(RoleItemType.Badge, badge.ResourceId, badge.ResourceNum, UnlimitedBadgeStack, NoItemLevel, BadgeManager.ShowNewTag);
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

        foreach (var outSideItem in NewPlayerDefaultGrants.RowsOf(gameData, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem))
        {
            if (bag.CreateOutSideItemGrant(outSideItem.ResourceId, outSideItem.ResourceNum) is not { } itemGrant)
            {
                continue;
            }

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
