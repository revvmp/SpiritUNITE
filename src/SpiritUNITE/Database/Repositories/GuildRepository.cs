using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Guild;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record GuildRewardCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    RoleWallet? Wallet,
    IReadOnlyDictionary<int, long> SpecialCoinsBefore,
    IReadOnlyList<BagItem> ChangedItems)
{
    public static GuildRewardCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, new Dictionary<int, long>(), []);
}

public sealed class GuildRepository(DataOptions options)
{
    public async Task<GuildMember?> FindMemberAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GuildMember>().FirstOrDefaultAsync(member => member.GameUlongUID == gameUlongUID);
    }

    public async Task<Guild?> FindGuildAsync(ulong guildID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<Guild>().FirstOrDefaultAsync(guild => guild.GuildID == guildID);
    }

    public async Task<Guild?> FindGuildByNameAsync(string guildName)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<Guild>().FirstOrDefaultAsync(guild => guild.GuildName == guildName);
    }

    public async Task<Guild[]> GetGuildsAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<Guild>().OrderBy(guild => guild.GuildID).ToArrayAsync();
    }

    public async Task<GuildMember[]> GetMembersAsync(ulong guildID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GuildMember>()
            .Where(member => member.GuildID == guildID)
            .OrderBy(member => member.JoinTime)
            .ThenBy(member => member.GameUlongUID)
            .ToArrayAsync();
    }

    public async Task<Dictionary<ulong, int>> CountMembersAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GuildMember>()
            .GroupBy(member => member.GuildID)
            .Select(group => new { GuildID = group.Key, Count = group.Count() })
            .ToDictionaryAsync(group => group.GuildID, group => group.Count);
    }

    public async Task<ulong?> CreateGuildAsync(Guild guild, ulong masterUid, int masterOffice)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            var guildID = Convert.ToUInt64(await database.InsertWithIdentityAsync(guild));
            await database.InsertAsync(new GuildMember
            {
                GameUlongUID = masterUid,
                GuildID = guildID,
                Office = masterOffice,
                JoinTime = guild.BuildTime,
            });
            await DeleteJoinRequestsOfRoleAsync(database, masterUid);
            await transaction.CommitAsync();
            return guildID;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return null;
        }
    }

    public async Task<bool> AddMemberAsync(GuildMember member)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(member);
            await DeleteJoinRequestsOfRoleAsync(database, member.GameUlongUID);
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
        await database.GetTable<GuildMember>().Where(member => member.GameUlongUID == gameUlongUID).DeleteAsync();
    }

    public async Task DeleteGuildAsync(ulong guildID)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<Guild>().Where(guild => guild.GuildID == guildID).DeleteAsync();
    }

    public async Task TransferMasterAsync(ulong oldMasterUid, int oldMasterOffice, ulong newMasterUid, int newMasterOffice)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.GetTable<GuildMember>()
            .Where(member => member.GameUlongUID == oldMasterUid)
            .Set(member => member.Office, oldMasterOffice)
            .UpdateAsync();
        await database.GetTable<GuildMember>()
            .Where(member => member.GameUlongUID == newMasterUid)
            .Set(member => member.Office, newMasterOffice)
            .UpdateAsync();
        await transaction.CommitAsync();
    }

    public async Task UpdateSettingAsync(Guild guild, bool clearJoinRequests)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.GetTable<Guild>()
            .Where(saved => saved.GuildID == guild.GuildID)
            .Set(saved => saved.Notice, guild.Notice)
            .Set(saved => saved.JoinType, guild.JoinType)
            .Set(saved => saved.Label1, guild.Label1)
            .Set(saved => saved.Label2, guild.Label2)
            .Set(saved => saved.Label3, guild.Label3)
            .UpdateAsync();
        if (clearJoinRequests)
        {
            await database.GetTable<GuildJoinRequest>().Where(request => request.GuildID == guild.GuildID).DeleteAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<GuildJoinRequest[]> GetJoinRequestsAsync(ulong guildID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GuildJoinRequest>()
            .Where(request => request.GuildID == guildID)
            .OrderBy(request => request.RequestTime)
            .ThenBy(request => request.GameUlongUID)
            .ToArrayAsync();
    }

    public async Task<bool> AddJoinRequestAsync(GuildJoinRequest request)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(request);
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<int> DeleteJoinRequestAsync(ulong guildID, ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GuildJoinRequest>()
            .Where(request => request.GuildID == guildID && request.GameUlongUID == gameUlongUID)
            .DeleteAsync();
    }

    public async Task<int> DeleteJoinRequestsAsync(ulong guildID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GuildJoinRequest>().Where(request => request.GuildID == guildID).DeleteAsync();
    }

    public async Task<GuildComment[]> GetCommentsAsync(ulong guildID, long afterCommentIndex)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GuildComment>()
            .Where(comment => comment.GuildID == guildID && comment.CommentIndex > afterCommentIndex)
            .OrderBy(comment => comment.CommentIndex)
            .ToArrayAsync();
    }

    public async Task<GuildComment> AddCommentAsync(GuildComment comment, int maxCommentNum)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var lastIndex = await database.GetTable<GuildComment>()
            .Where(saved => saved.GuildID == comment.GuildID)
            .Select(saved => (long?)saved.CommentIndex)
            .MaxAsync();
        comment.CommentIndex = (lastIndex ?? 0) + 1;
        await database.InsertAsync(comment);
        var keptFrom = comment.CommentIndex - maxCommentNum;
        await database.GetTable<GuildComment>()
            .Where(saved => saved.GuildID == comment.GuildID && saved.CommentIndex <= keptFrom)
            .DeleteAsync();
        await transaction.CommitAsync();
        return comment;
    }

    public async Task<RoleGuild> GetRoleGuildAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleGuild>().FirstOrDefaultAsync(saved => saved.GameUlongUID == gameUlongUID)
            ?? new RoleGuild { GameUlongUID = gameUlongUID };
    }

    public async Task SaveRoleGuildAsync(RoleGuild roleGuild)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(roleGuild);
    }

    public async Task<GuildActivity?> FindActivityAsync(ulong guildID, int periodID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<GuildActivity>()
            .FirstOrDefaultAsync(activity => activity.GuildID == guildID && activity.PeriodID == periodID);
    }

    public async Task SaveActivityRewardAsync(ulong guildID, int periodID, int rewardID)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<GuildActivity>()
            .InsertOrUpdateAsync(
                () => new GuildActivity { GuildID = guildID, PeriodID = periodID, RewardID = rewardID },
                activity => new GuildActivity { RewardID = rewardID });
    }

    public async Task<int[]> GetActivityRewardGearsAsync(ulong gameUlongUID, int periodID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleGuildActivityReward>()
            .Where(reward => reward.GameUlongUID == gameUlongUID && reward.PeriodID == periodID)
            .OrderBy(reward => reward.RewardGear)
            .Select(reward => reward.RewardGear)
            .ToArrayAsync();
    }

    public async Task<RoleGuildActivityTask[]> GetActivityTaskRewardsAsync(ulong gameUlongUID, long periodStartSince)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleGuildActivityTask>()
            .Where(reward => reward.GameUlongUID == gameUlongUID && reward.PeriodStart >= periodStartSince)
            .ToArrayAsync();
    }

    public async Task<GuildRewardCommit> CommitActivityTaskRewardAsync(RoleGuildActivityTask reward, int periodID, int maxGuildScore, GuildRewardGrant grant)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TryInsertAsync(database, reward))
        {
            await transaction.RollbackAsync();
            return GuildRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        }

        if (grant.GuildScore > 0 && await database.GetTable<GuildActivity>()
                .Where(activity => activity.GuildID == reward.GuildID && activity.PeriodID == periodID)
                .Set(activity => activity.GuildScore, activity => activity.GuildScore + grant.GuildScore > maxGuildScore
                    ? maxGuildScore
                    : activity.GuildScore + grant.GuildScore)
                .UpdateAsync() == 0)
        {
            await transaction.RollbackAsync();
            return GuildRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        return await ApplyRoleRewardAsync(database, transaction, reward.GameUlongUID, reward.RewardTime, grant);
    }

    public async Task<GuildRewardCommit> CommitActivityGearRewardAsync(RoleGuildActivityReward reward, GuildRewardGrant grant)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TryInsertAsync(database, reward))
        {
            await transaction.RollbackAsync();
            return GuildRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        return await ApplyRoleRewardAsync(database, transaction, reward.GameUlongUID, reward.RewardTime, grant);
    }

    public static async Task<GuildRewardCommit> ApplyRoleRewardAsync(
        DataConnection database,
        DataConnectionTransaction transaction,
        ulong gameUlongUID,
        long rewardTime,
        GuildRewardGrant grant)
    {
        foreach (var item in grant.Items)
        {
            if (!await TryInsertAsync(database, RoleRepository.ToEntity(gameUlongUID, item)))
            {
                await transaction.RollbackAsync();
                return GuildRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
            }
        }

        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var coinID in grant.Change.CoinGrants.Keys.Where(CoinID.IsSpecialCoin))
        {
            specialCoinsBefore[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, grant.Change, rewardTime);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return GuildRewardCommit.Refused(applied);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, grant.Change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new GuildRewardCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet, specialCoinsBefore, changedItems);
    }

    private static async Task<bool> TryInsertAsync<T>(DataConnection database, T row)
        where T : notnull
    {
        try
        {
            await database.InsertAsync(row);
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    private static Task<int> DeleteJoinRequestsOfRoleAsync(DataConnection database, ulong gameUlongUID) =>
        database.GetTable<GuildJoinRequest>().Where(request => request.GameUlongUID == gameUlongUID).DeleteAsync();
}
