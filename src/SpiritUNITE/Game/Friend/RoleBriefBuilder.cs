using SpiritUNITE.Auth;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Newbie;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Room;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Game.TenGames;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Friend;

public sealed class RoleBriefBuilder(
    GameData gameData,
    ProfileManager profiles,
    SettingsManager settings,
    NewbieManager newbies,
    AccountManager accounts,
    FriendPresence presence,
    RankedManager ranked,
    TenGamesManager tenGames,
    FabulousRepository fabulous)
{
    private const int GuestAccount = 1;
    private const int RegisteredAccount = 0;
    private const int NoRankedDivision = 0;
    private const int NoRankedBattles = 0;

    public async Task<pbCOMDT_RANKED_DIVISION_INFO_IN_BRIEF> BuildRankedDivisionInfoAsync(ulong uid)
    {
        var season = (await ranked.LoadAsync(uid)).Season;
        return RoleBrief.BuildRankedDivisionInfo(season.SeasonId, season.BattleCnt > NoRankedBattles ? season.Position.RankedDivision : NoRankedDivision);
    }

    public async Task<pbCOMDT_ROLE_BRIEF_INFO> BuildRoleBriefInfoAsync(LMNRoleInfo role, long lastLogoutTime)
    {
        var uid = role.GameUlongUID;
        var style = await profiles.LoadAsync(uid);
        var clientSettings = await settings.LoadAsync(uid);
        var beFabulous = await fabulous.FindBeFabulousAsync(uid);
        return new pbCOMDT_ROLE_BRIEF_INFO
        {
            Uid = uid,
            RoleName = role.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            RoleGender = role.Gender,
            LastLogoutTime = presence.IsOnline(uid) ? 0 : lastLogoutTime,
            RankedDivisionInfo = await BuildRankedDivisionInfoAsync(uid),
            HeadID = RoleImage.GetHeadID(gameData, role),
            RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
            CurHeadIconInfo = profiles.BuildHeadIconInfo(role, style).CurHeadIconInfo,
            PrivilePlatInfo = new pbCOMDT_PRIVILE_PLAT_INFO(),
            IsGuestAccount = (await accounts.FindBoundChannelsAsync(uid)).Contains(LoginPlatform.GUEST) ? GuestAccount : RegisteredAccount,
            ABModeType = clientSettings.ABMode,
            ClientVersion = BattleVersion.Build(),
            RegisterTime = await newbies.GetRegisterTimeAsync(role),
            TrainerBriefInfo = new pbCOMDT_TRAINER_BRIEF_INFO
            {
                TrainerImageInfo = ProfileManager.BuildProfileImageInfo(role, style).TrainerImageInfo,
            },
            UserPlantInfo = style.BuildUserPlantInfo(),
            TenGamesTeamID = await tenGames.FindTeamIDAsync(uid),
            BeFabulousCnt = beFabulous?.BeFabulousCnt ?? 0,
            BeFablsCntLastUpdTime = beFabulous?.BeFblsCntLastUpdTime ?? 0,
        };
    }
}
