using SpiritUNITE.Auth;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Friend;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using GuildEntity = SpiritUNITE.Database.Models.Guild;

namespace SpiritUNITE.Game.Guild;

public sealed class GuildInfoBuilder(
    GameData gameData,
    RoleInfoStore roles,
    ProfileManager profiles,
    RankedManager ranked,
    SettingsManager settings,
    AccountManager accounts,
    FriendPresence presence)
{
    private const int GuestAccount = 1;
    private const int RegisteredAccount = 0;
    private const int HasGuild = 1;

    public static pbCOMDT_ACNT_GUILD_INFO BuildAcntGuildInfo(GuildEntity? guild, GuildMember? member) =>
        guild is null || member is null
            ? new pbCOMDT_ACNT_GUILD_INFO()
            : new pbCOMDT_ACNT_GUILD_INFO
            {
                GuildID = (long)guild.GuildID,
                GuildName = guild.GuildName,
                Office = member.Office,
                HasGuild = HasGuild,
            };

    public static pbCOMDT_GUILD_BRIEF_INFO BuildBriefInfo(GuildEntity guild, LMNRoleInfo? master) => new()
    {
        GuildID = (long)guild.GuildID,
        GuildName = guild.GuildName,
        GuildShortId = GuildRules.BuildShortId(guild.GuildID),
        JoinType = guild.JoinType,
        MasterUid = master?.GameUlongUID ?? 0,
        MasterName = master?.InGameUsrName ?? "",
        LabelList = { GuildRules.LabelsOf(guild) },
    };

    public static pbCOMDT_GUILD_INFO BuildGuildInfo(GuildEntity guild, LMNRoleInfo? master, pbCOMDT_GUILD_ACTIVITY_BRIEF_INFO activityInfo) =>
        EmptyChildren.Fill(new pbCOMDT_GUILD_INFO
        {
            BriefInfo = BuildBriefInfo(guild, master),
            BuildTime = guild.BuildTime,
            Notice = guild.Notice,
            ActivityInfo = activityInfo,
        });

    public async Task<pbCOMDT_HEAD_INFO> BuildHeadInfoAsync(LMNRoleInfo role)
    {
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var (headType, curHeadResID) = style.CurPersonStyleHead;
        return EmptyChildren.Fill(new pbCOMDT_HEAD_INFO
        {
            Gender = role.Gender,
            HeadId = RoleImage.GetHeadID(gameData, role),
            AvatarInfo = role.RoleAvatarInfo.Clone(),
            HeadType = headType,
            CurPersonStyleInfo = new pbCOMDT_PLAYER_PERSON_STYLE_CUR_INFO { CurHeadResID = curHeadResID },
        });
    }

    public async Task<pbCOMDT_HEAD_ICON_BASE_INFO> BuildCurHeadIconInfoAsync(LMNRoleInfo role)
    {
        var style = await profiles.LoadAsync(role.GameUlongUID);
        return profiles.BuildHeadIconInfo(role, style).CurHeadIconInfo ?? new pbCOMDT_HEAD_ICON_BASE_INFO();
    }

    public async Task<pbCOMDT_ROLE_HEAD_URL_INFO> BuildUserPlantInfoAsync(LMNRoleInfo role) =>
        (await profiles.LoadAsync(role.GameUlongUID)).BuildUserPlantInfo();

    public async Task<pbCOMDT_GUILD_MEMBER_INFO> BuildMemberInfoAsync(GuildMember member, LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        await ranked.LoadAsync(uid);
        return EmptyChildren.Fill(new pbCOMDT_GUILD_MEMBER_INFO
        {
            Uid = uid,
            RoleName = role.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            RankedDivision = ranked.BuildRankedDivision(uid),
            HeadInfo = await BuildHeadInfoAsync(role),
            Office = member.Office,
            GameState = presence.BuildGameState(uid),
            ABMode = (await settings.LoadAsync(uid)).ABMode,
            IsGuestAccount = await FindIsGuestAccountAsync(uid),
            CurHeadIconInfo = await BuildCurHeadIconInfoAsync(role),
        });
    }

    public async Task<pbCOMDT_GUILD_MEMBER_INFO[]> BuildMemberInfosAsync(IEnumerable<GuildMember> members)
    {
        var infos = new List<pbCOMDT_GUILD_MEMBER_INFO>();
        foreach (var member in members)
        {
            if (await roles.FindAsync(member.GameUlongUID) is { } role)
            {
                infos.Add(await BuildMemberInfoAsync(member, role));
            }
        }

        return [.. infos];
    }

    public async Task<pbCOMDT_GUILD_JOIN_REQUEST_INFO> BuildJoinRequestInfoAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        await ranked.LoadAsync(uid);
        return EmptyChildren.Fill(new pbCOMDT_GUILD_JOIN_REQUEST_INFO
        {
            Uid = uid,
            RoleName = role.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            RankedDivision = ranked.BuildRankedDivision(uid),
            HeadInfo = await BuildHeadInfoAsync(role),
            ABMode = (await settings.LoadAsync(uid)).ABMode,
            IsGuestAccount = await FindIsGuestAccountAsync(uid),
            CurHeadIconInfo = await BuildCurHeadIconInfoAsync(role),
        });
    }

    public async Task<pbCOMDT_GUILD_JOIN_REQUEST_INFO[]> BuildJoinRequestInfosAsync(IEnumerable<GuildJoinRequest> requests)
    {
        var infos = new List<pbCOMDT_GUILD_JOIN_REQUEST_INFO>();
        foreach (var request in requests)
        {
            if (await roles.FindAsync(request.GameUlongUID) is { } role)
            {
                infos.Add(await BuildJoinRequestInfoAsync(role));
            }
        }

        return [.. infos];
    }

    private async Task<int> FindIsGuestAccountAsync(ulong uid) =>
        (await accounts.FindBoundChannelsAsync(uid)).Contains(LoginPlatform.GUEST) ? GuestAccount : RegisteredAccount;
}
