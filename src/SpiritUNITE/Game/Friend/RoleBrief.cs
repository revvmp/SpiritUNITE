using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Friend;

public static class RoleBrief
{
    public static pbCOMDT_RANKED_DIVISION_INFO_IN_BRIEF BuildRankedDivisionInfo(int seasonId, int rankedDivision) => new()
    {
        RankedDivisionSeasonId = seasonId,
        RankedDivision = rankedDivision,
    };

    public static pbCOMDT_HEAD_INFO BuildHeadInfo(LMNRoleInfo role) => EmptyChildren.Fill(new pbCOMDT_HEAD_INFO
    {
        Gender = role.Gender,
        AvatarInfo = role.RoleAvatarInfo.Clone(),
    });

    public static pbCOMDT_CHAT_PLAYER_INFO BuildChatPlayerInfo(LMNRoleInfo role) => EmptyChildren.Fill(new pbCOMDT_CHAT_PLAYER_INFO
    {
        Uid = role.GameUlongUID,
        Name = role.InGameUsrName,
        Gender = role.Gender,
        RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
    });
}
