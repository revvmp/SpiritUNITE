using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed class FriendRepository(DataOptions options)
{
    public async Task<RoleSocial> GetSocialAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleSocial>().FirstOrDefaultAsync(social => social.GameUlongUID == gameUlongUID)
            ?? new RoleSocial { GameUlongUID = gameUlongUID };
    }

    public async Task<Dictionary<ulong, long>> GetLastLogoutTimesAsync(IReadOnlyCollection<ulong> gameUlongUIDs)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleSocial>()
            .Where(social => gameUlongUIDs.Contains(social.GameUlongUID))
            .ToDictionaryAsync(social => social.GameUlongUID, social => social.LastLogoutTime);
    }

    public async Task SaveLastLogoutTimeAsync(ulong gameUlongUID, long lastLogoutTime)
    {
        await using var database = new DataConnection(options);
        await database.ExecuteAsync(
            "INSERT INTO role_social (game_ulong_uid, last_logout_time) SELECT game_ulong_uid, @logout FROM role WHERE game_ulong_uid=@uid ON DUPLICATE KEY UPDATE last_logout_time=VALUES(last_logout_time)",
            new DataParameter("uid", gameUlongUID),
            new DataParameter("logout", lastLogoutTime));
    }

    public async Task SaveDenyAllRequestFlagAsync(ulong gameUlongUID, int denyAllRequestFlag)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleSocial>()
            .InsertOrUpdateAsync(
                () => new RoleSocial { GameUlongUID = gameUlongUID, DenyAllRequestFlag = denyAllRequestFlag },
                social => new RoleSocial { DenyAllRequestFlag = denyAllRequestFlag });
    }

    public async Task SaveAcceptChatConventionAsync(ulong gameUlongUID, int acceptChatConvention)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleSocial>()
            .InsertOrUpdateAsync(
                () => new RoleSocial { GameUlongUID = gameUlongUID, AcceptChatConvention = acceptChatConvention },
                social => new RoleSocial { AcceptChatConvention = acceptChatConvention });
    }

    public async Task<RoleFriend[]> GetFriendsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriend>()
            .Where(friend => friend.GameUlongUID == gameUlongUID)
            .OrderBy(friend => friend.AddFriendTime)
            .ToArrayAsync();
    }

    public async Task<RoleFriend[]> GetFriendsAsync(ulong gameUlongUID, IReadOnlyCollection<ulong> friendUids)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriend>()
            .Where(friend => friend.GameUlongUID == gameUlongUID && friendUids.Contains(friend.FriendUid))
            .ToArrayAsync();
    }

    public async Task<RoleFriend[]> GetFriendshipsAmongAsync(IReadOnlyCollection<ulong> gameUlongUIDs)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriend>()
            .Where(friend => gameUlongUIDs.Contains(friend.GameUlongUID) && gameUlongUIDs.Contains(friend.FriendUid))
            .ToArrayAsync();
    }

    public static Task<int> AddIntimacyAsync(
        DataConnection database,
        ulong gameUlongUID,
        IReadOnlyCollection<ulong> friendUids,
        int intimacyValue) =>
        database.GetTable<RoleFriend>()
            .Where(friend => friend.GameUlongUID == gameUlongUID && friendUids.Contains(friend.FriendUid))
            .Set(friend => friend.IntimacyValue, friend => friend.IntimacyValue + intimacyValue)
            .UpdateAsync();

    public static Task<int> AddTogetherBattleAsync(
        DataConnection database,
        ulong gameUlongUID,
        IReadOnlyCollection<ulong> friendUids,
        int intimacyValue) =>
        database.GetTable<RoleFriend>()
            .Where(friend => friend.GameUlongUID == gameUlongUID && friendUids.Contains(friend.FriendUid))
            .Set(friend => friend.IntimacyValue, friend => friend.IntimacyValue + intimacyValue)
            .Set(friend => friend.TogetherBattleCnt, friend => friend.TogetherBattleCnt + 1)
            .UpdateAsync();

    public async Task<RoleFriend?> FindFriendAsync(ulong gameUlongUID, ulong friendUid)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriend>()
            .FirstOrDefaultAsync(friend => friend.GameUlongUID == gameUlongUID && friend.FriendUid == friendUid);
    }

    public async Task<RoleFriendRequest[]> GetRequestsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriendRequest>()
            .Where(request => request.GameUlongUID == gameUlongUID)
            .OrderBy(request => request.RequestTime)
            .ToArrayAsync();
    }

    public async Task<bool> HasRequestAsync(ulong gameUlongUID, ulong requestUid)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriendRequest>()
            .AnyAsync(request => request.GameUlongUID == gameUlongUID && request.RequestUid == requestUid);
    }

    public async Task SaveRequestAsync(RoleFriendRequest request, int maxRequestNum)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.InsertOrReplaceAsync(request);
        var overflow = await database.GetTable<RoleFriendRequest>()
            .Where(saved => saved.GameUlongUID == request.GameUlongUID)
            .OrderByDescending(saved => saved.RequestTime)
            .ThenByDescending(saved => saved.RequestUid)
            .Skip(maxRequestNum)
            .Select(saved => saved.RequestUid)
            .ToArrayAsync();
        if (overflow.Length > 0)
        {
            await database.GetTable<RoleFriendRequest>()
                .Where(saved => saved.GameUlongUID == request.GameUlongUID && overflow.Contains(saved.RequestUid))
                .DeleteAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<int> DeleteRequestAsync(ulong gameUlongUID, ulong requestUid)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriendRequest>()
            .Where(request => request.GameUlongUID == gameUlongUID && request.RequestUid == requestUid)
            .DeleteAsync();
    }

    public async Task<int> DeleteAllRequestsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriendRequest>()
            .Where(request => request.GameUlongUID == gameUlongUID)
            .DeleteAsync();
    }

    public async Task<PB_ENUM_RES_ERROR_CODE> MakeFriendsAsync(ulong gameUlongUID, ulong friendUid, long now, int maxFriendNum)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.GetTable<RoleFriendRequest>()
            .Where(request => (request.GameUlongUID == gameUlongUID && request.RequestUid == friendUid)
                || (request.GameUlongUID == friendUid && request.RequestUid == gameUlongUID))
            .DeleteAsync();

        if (await database.GetTable<RoleFriend>().AnyAsync(friend => friend.GameUlongUID == gameUlongUID && friend.FriendUid == friendUid))
        {
            await transaction.CommitAsync();
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendAlready;
        }

        if (await CountFriendsAsync(database, gameUlongUID) >= maxFriendNum)
        {
            await transaction.CommitAsync();
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendSelfFull;
        }

        if (await CountFriendsAsync(database, friendUid) >= maxFriendNum)
        {
            await transaction.CommitAsync();
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendTargetFull;
        }

        try
        {
            await database.InsertAsync(new RoleFriend { GameUlongUID = gameUlongUID, FriendUid = friendUid, AddFriendTime = now });
            await database.InsertAsync(new RoleFriend { GameUlongUID = friendUid, FriendUid = gameUlongUID, AddFriendTime = now });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return PB_ENUM_RES_ERROR_CODE.PbErrFriendAlready;
        }

        await transaction.CommitAsync();
        return PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
    }

    public async Task<bool> DeleteFriendshipAsync(ulong gameUlongUID, ulong friendUid)
    {
        await using var database = new DataConnection(options);
        var deleted = await database.GetTable<RoleFriend>()
            .Where(friend => (friend.GameUlongUID == gameUlongUID && friend.FriendUid == friendUid)
                || (friend.GameUlongUID == friendUid && friend.FriendUid == gameUlongUID))
            .DeleteAsync();
        return deleted > 0;
    }

    public async Task<bool> SetBanChatFlagAsync(ulong gameUlongUID, ulong friendUid, int banChatFlag)
    {
        await using var database = new DataConnection(options);
        var updated = await database.GetTable<RoleFriend>()
            .Where(friend => friend.GameUlongUID == gameUlongUID && friend.FriendUid == friendUid)
            .Set(friend => friend.BanChatFlag, banChatFlag)
            .UpdateAsync();
        return updated > 0;
    }

    public async Task<RoleFriendBlack[]> GetBlackListAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriendBlack>()
            .Where(black => black.GameUlongUID == gameUlongUID)
            .OrderBy(black => black.AddTime)
            .ToArrayAsync();
    }

    public async Task<bool> IsBlackedAsync(ulong gameUlongUID, ulong blackUid)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFriendBlack>()
            .AnyAsync(black => black.GameUlongUID == gameUlongUID && black.BlackUid == blackUid);
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, bool WasFriend)> AddBlackAsync(
        ulong gameUlongUID,
        ulong blackUid,
        long now,
        int maxBlackListLen)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (await database.GetTable<RoleFriendBlack>().AnyAsync(black => black.GameUlongUID == gameUlongUID && black.BlackUid == blackUid))
        {
            await transaction.CommitAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, false);
        }

        if (await database.GetTable<RoleFriendBlack>().CountAsync(black => black.GameUlongUID == gameUlongUID) >= maxBlackListLen)
        {
            await transaction.CommitAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrFriendBlackListFull, false);
        }

        var wasFriend = await database.GetTable<RoleFriend>()
            .Where(friend => (friend.GameUlongUID == gameUlongUID && friend.FriendUid == blackUid)
                || (friend.GameUlongUID == blackUid && friend.FriendUid == gameUlongUID))
            .DeleteAsync() > 0;
        await database.GetTable<RoleFriendRequest>()
            .Where(request => (request.GameUlongUID == gameUlongUID && request.RequestUid == blackUid)
                || (request.GameUlongUID == blackUid && request.RequestUid == gameUlongUID))
            .DeleteAsync();
        await database.InsertAsync(new RoleFriendBlack { GameUlongUID = gameUlongUID, BlackUid = blackUid, AddTime = now });
        await transaction.CommitAsync();
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wasFriend);
    }

    public async Task<bool> DeleteBlackAsync(ulong gameUlongUID, ulong blackUid)
    {
        await using var database = new DataConnection(options);
        var deleted = await database.GetTable<RoleFriendBlack>()
            .Where(black => black.GameUlongUID == gameUlongUID && black.BlackUid == blackUid)
            .DeleteAsync();
        return deleted > 0;
    }

    public async Task<ulong[]> SearchRoleUidsByNameAsync(string namePrefix, int limit)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<Role>()
            .Where(role => role.InGameUsrName.StartsWith(namePrefix))
            .OrderBy(role => role.InGameUsrName)
            .Take(limit)
            .Select(role => role.GameUlongUID)
            .ToArrayAsync();
    }

    private static Task<int> CountFriendsAsync(DataConnection database, ulong gameUlongUID) =>
        database.GetTable<RoleFriend>().CountAsync(friend => friend.GameUlongUID == gameUlongUID);
}
