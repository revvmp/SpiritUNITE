using SpiritUNITE.Database.Models;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.TenGames;

public sealed class TenGamesTeamInfoBuilder(
    GameData gameData,
    RoleInfoStore roles,
    ProfileManager profiles,
    RankedManager ranked)
{
    public static pbCOMDT_TEN_GAMES_TEAM_INFO NoTeam => new();

    public async Task<pbCOMDT_TEN_GAMES_TEAM_INFO> BuildTeamInfoAsync(TenGamesTeam team, IEnumerable<ulong> memberUids) => new()
    {
        TeamID = (long)team.TeamID,
        TeamName = team.TeamName,
        TeamMemberList = { await BuildMemberInfosAsync(memberUids) },
        TeamLeaderUid = team.TeamLeaderUid,
        JoinTenGamesID = team.JoinTenGamesID,
        CurScore = team.CurScore,
        WinCount = team.WinCount,
        LoseCount = team.LoseCount,
    };

    public async Task<pbCOMDT_TEN_GAMES_TEAM_MEMBER_INFO[]> BuildMemberInfosAsync(IEnumerable<ulong> memberUids)
    {
        var infos = new List<pbCOMDT_TEN_GAMES_TEAM_MEMBER_INFO>();
        foreach (var uid in memberUids)
        {
            if (await roles.FindAsync(uid) is { } role)
            {
                infos.Add(await BuildMemberInfoAsync(role));
            }
        }

        return [.. infos];
    }

    public async Task<pbCOMDT_TEN_GAMES_TEAM_MEMBER_INFO> BuildMemberInfoAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        await ranked.LoadAsync(uid);
        var style = await profiles.LoadAsync(uid);
        return EmptyChildren.Fill(new pbCOMDT_TEN_GAMES_TEAM_MEMBER_INFO
        {
            Uid = uid,
            HeaderId = RoleImage.GetHeadID(gameData, role),
            Name = role.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            RankedDivision = ranked.PositionOf(uid).RankedDivision,
            Gender = role.Gender,
            RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
            CurHeadIconInfo = profiles.BuildHeadIconInfo(role, style).CurHeadIconInfo,
            TrainerImageInfo = ProfileManager.BuildProfileImageInfo(role, style).TrainerImageInfo,
            UserPlantInfo = style.BuildUserPlantInfo(),
        });
    }
}
