using SpiritUNITE.Databin;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Friend;

public static class RoleBrief
{
    public static pbCOMDT_RANKED_DIVISION_INFO_IN_BRIEF BuildRankedDivisionInfo(GameData gameData, DateTimeOffset now) => new()
    {
        RankedDivisionSeasonId = RankedMatch.FindCurrentSeason(gameData, now).Season.SeasonId,
        RankedDivision = (int)RankedMatch.StartingRankedDivision,
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
