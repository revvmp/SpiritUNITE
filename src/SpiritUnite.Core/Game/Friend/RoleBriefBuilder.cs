using SpiritUnite.Auth;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Newbie;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Room;
using SpiritUnite.Game.Settings;
using SpiritUnite.Game.TenGames;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Friend;

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
