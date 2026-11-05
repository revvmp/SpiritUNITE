using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Newbie;

public sealed class NewbieChallengeManager(
    GameData gameData,
    LobbyNotices notices,
    RoleRepository roleRepository,
    TimeProvider clock)
{
    private const int NewbieChallengeOpenDays = 32;
    private const int FirstUnlockDay = 1;
    private const int MaxNewbieChallengeDayNum = (int)PB_MACRO_COM_MAX_NEWBIE_CHALLENGE_DAY_NUM.PbComMaxNewbieChallengeDayNum;

    public async Task<pbCOMDT_NEWBIE_CHALLENGE_UNLOCK_INFO> BuildUnlockInfoAsync(LMNRoleInfo role)
    {
        if (!IsUnlocked(role))
        {
            return new pbCOMDT_NEWBIE_CHALLENGE_UNLOCK_INFO();
        }

        var registerDay = await FindRegisterDayAsync(role);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        return new pbCOMDT_NEWBIE_CHALLENGE_UNLOCK_INFO
        {
            LeftTime = (int)Math.Max(0, FindCloseTime(registerDay) - now.ToUnixTimeSeconds()),
            UnlockDays = Math.Clamp(today.DayNumber - registerDay.DayNumber + FirstUnlockDay, FirstUnlockDay, MaxNewbieChallengeDayNum),
        };
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_NEWBIE_CHALLENGE_UNLOCK_INFO UnlockInfo)> GetNewbieChallengeAsync(LMNRoleInfo role)
    {
        if (!IsUnlocked(role))
        {
            return (PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeUnlock, new pbCOMDT_NEWBIE_CHALLENGE_UNLOCK_INFO());
        }

        var unlockInfo = await BuildUnlockInfoAsync(role);
        return unlockInfo.LeftTime > 0
            ? (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, unlockInfo)
            : (PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeClosed, unlockInfo);
    }

    public bool IsUnlocked(LMNRoleInfo role) =>
        gameData.SystemUnlock.Find(pbSystemUnlock.Key(
            (int)PB_ENUM_RES_SYSTEM_UNLOCK_TYPE.PbResSystemUnlockTypeNewChallenge,
            notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeSystemUnlock))) is { } unlock
        && role.RoleTotalExp >= unlock.TrainerExp;

    private async Task<DateOnly> FindRegisterDayAsync(LMNRoleInfo role)
    {
        var registerTime = await roleRepository.FindCreateTimeAsync(role.GameUlongUID);
        return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(registerTime).UtcDateTime);
    }

    private static long FindCloseTime(DateOnly registerDay) =>
        new DateTimeOffset(registerDay.AddDays(NewbieChallengeOpenDays).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            .ToUnixTimeSeconds() - 1;
}
