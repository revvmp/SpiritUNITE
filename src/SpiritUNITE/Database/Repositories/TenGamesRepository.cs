using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Guild;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record TenGamesArchivedTeam(TenGamesHistory History, IReadOnlyList<ulong> MemberUids);

public sealed class TenGamesRepository(DataOptions options)
{
    private const int NotJoined = 0;
    private const int NotBought = 0;
    private const int Bought = 1;

    public async Task<TenGamesTeamMember?> FindMemberAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<TenGamesTeamMember>().FirstOrDefaultAsync(member => member.GameUlongUID == gameUlongUID);
    }

    public async Task<TenGamesTeam?> FindTeamAsync(ulong teamID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<TenGamesTeam>().FirstOrDefaultAsync(team => team.TeamID == teamID);
    }

    public async Task<TenGamesTeamMember[]> GetMembersAsync(ulong teamID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<TenGamesTeamMember>()
            .Where(member => member.TeamID == teamID)
            .OrderBy(member => member.JoinTime)
            .ThenBy(member => member.GameUlongUID)
            .ToArrayAsync();
    }

    public async Task<TenGamesTeam[]> GetTeamsOfGameAsync(int gamesId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<TenGamesTeam>().Where(team => team.JoinTenGamesID == gamesId).ToArrayAsync();
    }

    public async Task<int[]> GetJoinedGamesIdsAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<TenGamesTeam>()
            .Where(team => team.JoinTenGamesID != NotJoined)
            .Select(team => team.JoinTenGamesID)
            .Distinct()
            .ToArrayAsync();
    }

    public async Task<ulong?> CreateTeamAsync(TenGamesTeam team)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            var teamID = Convert.ToUInt64(await database.InsertWithIdentityAsync(team));
            await database.InsertAsync(new TenGamesTeamMember { GameUlongUID = team.TeamLeaderUid, TeamID = teamID, JoinTime = team.CreateTime });
            await DeleteInvitesOfRoleAsync(database, team.TeamLeaderUid);
            await SaveTeamCreateTimeAsync(database, team.TeamLeaderUid, team.CreateTime);
            await transaction.CommitAsync();
            return teamID;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return null;
        }
    }

    public async Task<bool> AddMemberAsync(TenGamesTeamMember member)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(member);
            await DeleteInvitesOfRoleAsync(database, member.GameUlongUID);
            await transaction.CommitAsync();
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return false;
        }
    }

    public async Task RemoveMemberAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<TenGamesTeamMember>().Where(member => member.GameUlongUID == gameUlongUID).DeleteAsync();
    }

    public async Task DeleteTeamAsync(ulong teamID)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<TenGamesTeam>().Where(team => team.TeamID == teamID).DeleteAsync();
    }

    public async Task<bool> RenameTeamAsync(ulong teamID, string teamName, long modifyNameTime)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.GetTable<TenGamesTeam>()
                .Where(team => team.TeamID == teamID)
                .Set(team => team.TeamName, teamName)
                .Set(team => team.ModifyNameTime, modifyNameTime)
                .UpdateAsync();
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task SetLeaderAsync(ulong teamID, ulong teamLeaderUid)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<TenGamesTeam>()
            .Where(team => team.TeamID == teamID)
            .Set(team => team.TeamLeaderUid, teamLeaderUid)
            .UpdateAsync();
    }

    public async Task ApplyAsync(ulong teamID, int gamesId, int initScore)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<TenGamesTeam>()
            .Where(team => team.TeamID == teamID)
            .Set(team => team.JoinTenGamesID, gamesId)
            .Set(team => team.CurScore, initScore)
            .Set(team => team.WinCount, 0)
            .Set(team => team.LoseCount, 0)
            .UpdateAsync();
    }

    public async Task SaveBattleAsync(TenGamesTeam team)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<TenGamesTeam>()
            .Where(stored => stored.TeamID == team.TeamID)
            .Set(stored => stored.CurScore, team.CurScore)
            .Set(stored => stored.WinCount, team.WinCount)
            .Set(stored => stored.LoseCount, team.LoseCount)
            .UpdateAsync();
    }

    public async Task<TenGamesTeamInvite?> FindInviteAsync(ulong inviteeUid, ulong teamID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<TenGamesTeamInvite>()
            .FirstOrDefaultAsync(invite => invite.GameUlongUID == inviteeUid && invite.TeamID == teamID);
    }

    public async Task<TenGamesTeamInvite[]> GetInvitesAsync(ulong inviteeUid)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<TenGamesTeamInvite>()
            .Where(invite => invite.GameUlongUID == inviteeUid)
            .OrderByDescending(invite => invite.InvitedTime)
            .ToArrayAsync();
    }

    public async Task<bool> AddInviteAsync(TenGamesTeamInvite invite)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(invite);
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task DeleteInviteAsync(ulong inviteeUid, ulong teamID)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<TenGamesTeamInvite>()
            .Where(invite => invite.GameUlongUID == inviteeUid && invite.TeamID == teamID)
            .DeleteAsync();
    }

    public async Task<RoleTenGames> GetRoleAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleTenGames>().FirstOrDefaultAsync(role => role.GameUlongUID == gameUlongUID)
            ?? new RoleTenGames { GameUlongUID = gameUlongUID };
    }

    public async Task SaveLastReadInviteTimeAsync(ulong gameUlongUID, long lastReadInviteTime)
    {
        await using var database = new DataConnection(options);
        var updated = await database.GetTable<RoleTenGames>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .Set(role => role.TenGamesLastReadInviteTime, lastReadInviteTime)
            .UpdateAsync();
        if (updated == 0)
        {
            await InsertRoleAsync(database, new RoleTenGames { GameUlongUID = gameUlongUID, TenGamesLastReadInviteTime = lastReadInviteTime });
        }
    }

    public async Task ArchiveGameAsync(int gamesId, IReadOnlyList<TenGamesArchivedTeam> teams)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        foreach (var team in teams)
        {
            await database.InsertAsync(team.History);
            foreach (var memberUid in team.MemberUids)
            {
                await database.InsertAsync(new TenGamesHistoryMember { GamesId = gamesId, TeamID = team.History.TeamID, GameUlongUID = memberUid });
            }
        }

        await database.GetTable<TenGamesTeam>()
            .Where(team => team.JoinTenGamesID == gamesId)
            .Set(team => team.JoinTenGamesID, NotJoined)
            .Set(team => team.CurScore, 0)
            .Set(team => team.WinCount, 0)
            .Set(team => team.LoseCount, 0)
            .UpdateAsync();
        await transaction.CommitAsync();
    }

    public async Task<TenGamesArchivedTeam[]> GetHistoryAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var histories = await (
            from member in database.GetTable<TenGamesHistoryMember>()
            join history in database.GetTable<TenGamesHistory>()
                on new { member.GamesId, member.TeamID } equals new { history.GamesId, history.TeamID }
            where member.GameUlongUID == gameUlongUID
            select history).ToArrayAsync();
        var archived = new List<TenGamesArchivedTeam>();
        foreach (var history in histories)
        {
            var memberUids = await database.GetTable<TenGamesHistoryMember>()
                .Where(member => member.GamesId == history.GamesId && member.TeamID == history.TeamID)
                .Select(member => member.GameUlongUID)
                .ToArrayAsync();
            archived.Add(new TenGamesArchivedTeam(history, memberUids));
        }

        return [.. archived];
    }

    public async Task<RoleTenGamesBP?> FindBPAsync(ulong gameUlongUID, int activityID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleTenGamesBP>()
            .FirstOrDefaultAsync(bp => bp.GameUlongUID == gameUlongUID && bp.ActivityID == activityID);
    }

    public async Task<RoleTenGamesBPReward[]> GetBPRewardsAsync(ulong gameUlongUID, int activityID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleTenGamesBPReward>()
            .Where(reward => reward.GameUlongUID == gameUlongUID && reward.ActivityID == activityID)
            .ToArrayAsync();
    }

    public async Task AddBPWinAsync(ulong gameUlongUID, int activityID)
    {
        await using var database = new DataConnection(options);
        var updated = await database.GetTable<RoleTenGamesBP>()
            .Where(bp => bp.GameUlongUID == gameUlongUID && bp.ActivityID == activityID)
            .Set(bp => bp.WinCnt, bp => bp.WinCnt + 1)
            .UpdateAsync();
        if (updated == 0)
        {
            try
            {
                await database.InsertAsync(new RoleTenGamesBP { GameUlongUID = gameUlongUID, ActivityID = activityID, WinCnt = 1 });
            }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                await database.GetTable<RoleTenGamesBP>()
                    .Where(bp => bp.GameUlongUID == gameUlongUID && bp.ActivityID == activityID)
                    .Set(bp => bp.WinCnt, bp => bp.WinCnt + 1)
                    .UpdateAsync();
            }
        }
    }

    public async Task<GuildRewardCommit> CommitBPRewardAsync(ulong gameUlongUID, long rewardTime, IReadOnlyList<RoleTenGamesBPReward> rewards, GuildRewardGrant grant)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            foreach (var reward in rewards)
            {
                await database.InsertAsync(reward);
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return GuildRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        return await GuildRepository.ApplyRoleRewardAsync(database, transaction, gameUlongUID, rewardTime, grant);
    }

    public async Task<GuildRewardCommit> CommitBPBuyAsync(ulong gameUlongUID, int activityID, long buyTime, GuildRewardGrant cost)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var bought = await database.GetTable<RoleTenGamesBP>()
            .Where(bp => bp.GameUlongUID == gameUlongUID && bp.ActivityID == activityID && bp.IsBuySenierReward == NotBought)
            .Set(bp => bp.IsBuySenierReward, Bought)
            .UpdateAsync();
        if (bought == 0)
        {
            try
            {
                await database.InsertAsync(new RoleTenGamesBP { GameUlongUID = gameUlongUID, ActivityID = activityID, IsBuySenierReward = Bought });
            }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                await transaction.RollbackAsync();
                return GuildRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTenGamesBpactivityRepeatBuy);
            }
        }

        return await GuildRepository.ApplyRoleRewardAsync(database, transaction, gameUlongUID, buyTime, cost);
    }

    private static Task<int> DeleteInvitesOfRoleAsync(DataConnection database, ulong gameUlongUID) =>
        database.GetTable<TenGamesTeamInvite>().Where(invite => invite.GameUlongUID == gameUlongUID).DeleteAsync();

    private static async Task SaveTeamCreateTimeAsync(DataConnection database, ulong gameUlongUID, long createTime)
    {
        var updated = await database.GetTable<RoleTenGames>()
            .Where(role => role.GameUlongUID == gameUlongUID)
            .Set(role => role.TenGamesTeamCreateTime, createTime)
            .UpdateAsync();
        if (updated == 0)
        {
            await database.InsertAsync(new RoleTenGames { GameUlongUID = gameUlongUID, TenGamesTeamCreateTime = createTime });
        }
    }

    private static async Task InsertRoleAsync(DataConnection database, RoleTenGames role)
    {
        try
        {
            await database.InsertAsync(role);
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await database.GetTable<RoleTenGames>()
                .Where(stored => stored.GameUlongUID == role.GameUlongUID)
                .Set(stored => stored.TenGamesLastReadInviteTime, role.TenGamesLastReadInviteTime)
                .UpdateAsync();
        }
    }
}
