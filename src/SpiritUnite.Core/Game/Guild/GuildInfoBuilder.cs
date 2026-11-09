using SpiritUnite.Auth;
using SpiritUnite.Database.Models;
using SpiritUnite.Databin;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Profile;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Settings;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using GuildEntity = SpiritUnite.Database.Models.Guild;

namespace SpiritUnite.Game.Guild;

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
    private const long FirstGuildCheckDelaySeconds = 3600;

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

    public static pbCOMDT_GUILD_INFO BuildCreatedGuildInfo(GuildEntity guild, LMNRoleInfo? master) => new()
    {
        BriefInfo = BuildBriefInfo(guild, master),
        BuildTime = guild.BuildTime,
        Notice = guild.Notice,
        NextCheckTime = guild.BuildTime + FirstGuildCheckDelaySeconds,
    };

    public static pbCOMDT_GUILD_INFO BuildGuildInfo(GuildEntity guild, LMNRoleInfo? master, pbCOMDT_GUILD_ACTIVITY_BRIEF_INFO activityInfo)
    {
        var guildInfo = BuildCreatedGuildInfo(guild, master);
        guildInfo.ActivityInfo = activityInfo;
        guildInfo.RoomList = new pbCOMDT_GUILD_ROOM_LIST();
        guildInfo.LastDumpTlogTime = guild.BuildTime;
        guildInfo.BossRushActivityInfo = new pbCOMDT_BOSS_RUSH_GUILD_ACTIVITY_BRIEF_INFO();
        return guildInfo;
    }

    public async Task<pbCOMDT_HEAD_INFO> BuildHeadInfoAsync(LMNRoleInfo role)
    {
        var style = await profiles.LoadAsync(role.GameUlongUID);
        var (headType, curHeadResID) = style.CurPersonStyleHead;
        return new pbCOMDT_HEAD_INFO
        {
            Gender = role.Gender,
            HeadId = RoleImage.GetHeadID(gameData, role),
            AvatarInfo = role.RoleAvatarInfo.Clone(),
            HeadType = headType,
            CurPersonStyleInfo = new pbCOMDT_PLAYER_PERSON_STYLE_CUR_INFO { CurHeadResID = curHeadResID },
        };
    }

    public static pbCOMDT_HEAD_INFO BuildUnknownHeadInfo() => new()
    {
        AvatarInfo = new pbCOMDT_ROLE_AVATAR_INFO(),
        CurPersonStyleInfo = new pbCOMDT_PLAYER_PERSON_STYLE_CUR_INFO(),
    };

    public static pbCOMDT_GUILD_MEMBER_INFO BuildDeletedMemberInfo(ulong uid, string roleName) => new()
    {
        Uid = uid,
        RoleName = roleName,
        RankedDivision = new pbCOMDT_RANKED_DIVISION(),
        HeadInfo = BuildUnknownHeadInfo(),
        GameState = EmptyChildren.Fill(new pbCOMDT_GAME_STATE_INFO()),
        CurHeadIconInfo = new pbCOMDT_HEAD_ICON_BASE_INFO(),
    };

    public static pbCOMDT_COMMENT_REDDOT_INFO? BuildCommentRedDotInfo(GuildMember member) =>
        member.CommentLastSendIndex is { } lastSendIndex && member.CommentLastReadIndex is { } lastReadIndex
            ? new pbCOMDT_COMMENT_REDDOT_INFO
            {
                RedDotDetail = new pbCOMDT_RED_DOT_DETAIL(),
                LastSendIndex = lastSendIndex,
                LastReadIndex = lastReadIndex,
            }
            : null;

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
        return new pbCOMDT_GUILD_MEMBER_INFO
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
            CommentRedDotInfo = BuildCommentRedDotInfo(member),
        };
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
        return new pbCOMDT_GUILD_JOIN_REQUEST_INFO
        {
            Uid = uid,
            RoleName = role.InGameUsrName,
            RoleLevel = RoleProfile.GetLevel(gameData, role),
            RankedDivision = ranked.BuildRankedDivision(uid),
            HeadInfo = await BuildHeadInfoAsync(role),
            ABMode = (await settings.LoadAsync(uid)).ABMode,
            IsGuestAccount = await FindIsGuestAccountAsync(uid),
            CurHeadIconInfo = await BuildCurHeadIconInfoAsync(role),
        };
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
