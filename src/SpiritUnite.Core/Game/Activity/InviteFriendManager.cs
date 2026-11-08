using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

public sealed record InviteFriendMailAwardInfo(int MailAwardOneCnt, int MailAwardTwoCnt, string FriendShortId);

public sealed class InviteFriendManager(
    InviteFriendRepository repository,
    RoleInfoStore roles,
    RoleRepository roleRepository,
    ActivitySchedule schedule,
    GameData gameData,
    TimeProvider clock,
    ILogger<InviteFriendManager> logger)
{
    private const long SecondsPerDay = 86400;
    private const string NoFriendShortId = "";
    private const int MailAwardOneID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidFriendInviteAwardOne;
    private const int MailAwardTwoID = (int)PB_ENUM_RES_MAILID_TYPE.PbResMailidFriendInviteAwardTwo;

    private readonly int friendInviteActivityId = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalFriendInviteActivityid);
    private readonly int friendInviterLevel = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalFriendInviterLevel);
    private readonly int friendBeInviterLevel = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalFriendBeInviterLevel);
    private readonly int friendBeInviterDay = gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalFriendBeInviterDay);

    public async Task<string> FindFriendShortIdAsync(ulong uid) =>
        await repository.FindFriendUidAsync(uid) is { } friendUid ? RoleProfile.BuildShortId(friendUid) : NoFriendShortId;

    public async Task<InviteFriendMailAwardInfo> GetMailAwardInfoAsync(ulong uid)
    {
        var counts = await repository.CountMailsAsync(uid, [MailAwardOneID, MailAwardTwoID]);
        return new InviteFriendMailAwardInfo(
            counts.GetValueOrDefault(MailAwardOneID),
            counts.GetValueOrDefault(MailAwardTwoID),
            await FindFriendShortIdAsync(uid));
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> BindAsync(LMNRoleInfo role, string friendShortId)
    {
        var uid = role.GameUlongUID;
        var result = await CheckBindAsync(role, friendShortId);
        if (result.Code == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            && !await repository.TryBindAsync(uid, result.FriendUid, clock.GetUtcNow().ToUnixTimeSeconds()))
        {
            result = (PB_ENUM_RES_ERROR_CODE.PbErrFriendBindEver, result.FriendUid);
        }

        logger.LogInformation("Invite friend bind of uid {Uid} to friend uid {FriendUid}: {Result}", uid, result.FriendUid, result.Code);
        return result.Code;
    }

    private async Task<(PB_ENUM_RES_ERROR_CODE Code, ulong FriendUid)> CheckBindAsync(LMNRoleInfo role, string friendShortId)
    {
        var now = clock.GetUtcNow();
        if (!schedule.IsOpen(friendInviteActivityId, PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeFriendInvite, now))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrFriendInviteActivityClose, 0);
        }

        if (!RoleProfile.TryParseShortId(friendShortId, out var friendUid))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrFriendBindShortIdNoData, 0);
        }

        if (friendUid == role.GameUlongUID)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrFriendBindFriendSelfInvalid, friendUid);
        }

        if (await repository.FindFriendUidAsync(role.GameUlongUID) is not null)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrFriendBindEver, friendUid);
        }

        if (RoleProfile.GetLevel(gameData, role) > friendBeInviterLevel)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrFriendBindSelfLevelLimit, friendUid);
        }

        var registeredDays = (now.ToUnixTimeSeconds() - await roleRepository.FindCreateTimeAsync(role.GameUlongUID)) / SecondsPerDay;
        if (registeredDays > friendBeInviterDay)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrFriendBindFriendRegisterLimit, friendUid);
        }

        var friend = roles.Find(friendUid) ?? await roleRepository.FindAsync(friendUid);
        if (friend is null)
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrFriendBindShortIdNoData, friendUid);
        }

        return RoleProfile.GetLevel(gameData, friend) < friendInviterLevel
            ? (PB_ENUM_RES_ERROR_CODE.PbErrFriendBindFriendLevelLimit, friendUid)
            : (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, friendUid);
    }
}
