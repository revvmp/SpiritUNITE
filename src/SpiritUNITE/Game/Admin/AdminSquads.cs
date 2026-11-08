using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Guild;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Admin;

public sealed record AdminSquadRow(ulong GuildID, string ShortId, string Name, AdminTrainer Leader, int Members, long BuildTime);

public sealed record AdminSquadMember(AdminTrainer Trainer, bool IsLeader, long JoinTime);

public sealed record AdminSquadDetail(AdminSquadRow Squad, string Notice, IReadOnlyList<AdminSquadMember> Members);

public sealed class AdminSquads(GuildRepository guilds, GuildManager guildManager, RoleRepository roles)
{
    public async Task<AdminSquadRow[]> ListAsync()
    {
        var all = await guilds.GetGuildsAsync();
        var counts = await guilds.CountMembersAsync();
        var leaders = new Dictionary<ulong, ulong>();
        foreach (var guild in all)
        {
            if (await LeaderOfAsync(guild.GuildID) is { } leaderUid)
            {
                leaders[guild.GuildID] = leaderUid;
            }
        }

        var names = await AdminNames.FindAsync(roles, leaders.Values);
        return
        [
            .. all.Select(guild => new AdminSquadRow(
                guild.GuildID,
                GuildRules.BuildShortId(guild.GuildID),
                guild.GuildName,
                names.Trainer(leaders.GetValueOrDefault(guild.GuildID)),
                counts.GetValueOrDefault(guild.GuildID),
                guild.BuildTime)),
        ];
    }

    public async Task<AdminSquadDetail?> FindAsync(ulong guildID)
    {
        if (await guilds.FindGuildAsync(guildID) is not { } guild)
        {
            return null;
        }

        var members = await guilds.GetMembersAsync(guildID);
        var names = await AdminNames.FindAsync(roles, [.. members.Select(member => member.GameUlongUID)]);
        var leaderUid = members.FirstOrDefault(member => member.Office == GuildRules.MasterOffice)?.GameUlongUID ?? 0;
        return new AdminSquadDetail(
            new AdminSquadRow(guild.GuildID, GuildRules.BuildShortId(guild.GuildID), guild.GuildName, names.Trainer(leaderUid), members.Length, guild.BuildTime),
            guild.Notice,
            [.. members.Select(member => new AdminSquadMember(names.Trainer(member.GameUlongUID), member.Office == GuildRules.MasterOffice, member.JoinTime))]);
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> RemoveMemberAsync(ulong guildID, ulong memberUid)
    {
        if (await LeaderOfAsync(guildID) is not { } leaderUid)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist;
        }

        var response = await guildManager.KickMemberAsync(leaderUid, memberUid);
        return (PB_ENUM_RES_ERROR_CODE)response.ResultInfo.ResultId;
    }

    private async Task<ulong?> LeaderOfAsync(ulong guildID) =>
        (await guilds.GetMembersAsync(guildID)).FirstOrDefault(member => member.Office == GuildRules.MasterOffice)?.GameUlongUID;
}
