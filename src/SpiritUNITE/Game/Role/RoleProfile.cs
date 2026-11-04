using System.Globalization;
using SpiritUNITE.Game.Data;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public static class RoleProfile
{
    public static int GetLevel(GameData gameData, LMNRoleInfo role) => gameData.TrainerLevel.Rows
        .Where(row => row.UpgradeExp <= role.RoleTotalExp)
        .Max(row => row.Level);

    public static int GetNextLevelTotalExp(GameData gameData, LMNRoleInfo role) => gameData.TrainerLevel.Rows
        .Where(row => row.UpgradeExp > role.RoleTotalExp)
        .Select(row => row.UpgradeExp)
        .DefaultIfEmpty(gameData.TrainerLevel.Rows.Max(row => row.UpgradeExp))
        .Min();

    public static bool HasReachedMaxLevel(GameData gameData, LMNRoleInfo role) =>
        role.RoleTotalExp >= gameData.TrainerLevel.Rows.Max(row => row.UpgradeExp);

    public static string BuildShortId(LMNRoleInfo role) =>
        role.GameUlongUID.ToString(CultureInfo.InvariantCulture);

    public static pbCSDT_PROFILE_DETAIL_NEW BuildProfileDetail(GameData gameData, LMNRoleInfo role) => new()
    {
        Uid = role.GameUlongUID,
        RoleName = role.InGameUsrName,
        RoleLevel = GetLevel(gameData, role),
        RoleGender = role.Gender,
        FollowInfo = role.BuildFollowInfo(),
        UserShortId = BuildShortId(role),
        PlayerContestantInfo = new pbCOMDT_PLAYER_CONTESTANT_INFO
        {
            TrainerImageInfo = new pbCOMDT_TRAINER_IMAGE_INFO(),
            ContestantResList = new pbCOMDT_PLAYER_CONTESTANT_RES_LIST(),
            RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
        },
    };

    public static pbSCProfileImageInfoRsp BuildProfileImageInfo(LMNRoleInfo role) => new()
    {
        ResultInfo = new pbResultInfo(),
        RoleAvatar = role.RoleAvatarInfo.Clone(),
        TrainerImageInfo = new pbCOMDT_TRAINER_IMAGE_INFO(),
        ShortId = BuildShortId(role),
        QueryUid = role.GameUlongUID,
    };
}
