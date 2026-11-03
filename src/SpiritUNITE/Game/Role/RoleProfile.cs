using System.Globalization;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public static class RoleProfile
{
    public const int StartingLevel = 1;

    public static string BuildShortId(LMNRoleInfo role) =>
        role.GameUlongUID.ToString(CultureInfo.InvariantCulture);

    public static pbCSDT_PROFILE_DETAIL_NEW BuildProfileDetail(LMNRoleInfo role) => new()
    {
        Uid = role.GameUlongUID,
        RoleName = role.InGameUsrName,
        RoleLevel = StartingLevel,
        RoleGender = role.Gender,
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
