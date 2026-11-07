using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed record VideoHistoryTip(int DelVideoGuideTip, int VideoDelByVersion);

public sealed class VideoHistoryRepository(DataOptions options)
{
    public async Task<RoleVideoHistory[]> ListAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleVideoHistory>()
            .Where(video => video.GameUlongUID == gameUlongUID)
            .OrderBy(video => video.SaveTime)
            .ToArrayAsync();
    }

    public async Task DeleteAsync(ulong gameUlongUID, IReadOnlyCollection<string> absIds)
    {
        if (absIds.Count == 0)
        {
            return;
        }

        await using var database = new DataConnection(options);
        await database.GetTable<RoleVideoHistory>()
            .Where(video => video.GameUlongUID == gameUlongUID && absIds.Contains(video.AbsId))
            .DeleteAsync();
    }

    public async Task<bool> SaveAsync(RoleVideoHistory video, IReadOnlyCollection<string> replacedAbsIds)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            if (replacedAbsIds.Count > 0)
            {
                await database.GetTable<RoleVideoHistory>()
                    .Where(saved => saved.GameUlongUID == video.GameUlongUID && replacedAbsIds.Contains(saved.AbsId))
                    .DeleteAsync();
            }

            await database.InsertAsync(video);
            await transaction.CommitAsync();
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return false;
        }
    }

    public async Task<bool> SetCollectFlagAsync(ulong gameUlongUID, string absId, int collectFlag)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleVideoHistory>()
            .Where(video => video.GameUlongUID == gameUlongUID && video.AbsId == absId)
            .Set(video => video.CollectFlag, collectFlag)
            .UpdateAsync() > 0;
    }

    public async Task<VideoHistoryTip> FindTipAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleVideoHistoryTip>().FirstOrDefaultAsync(tip => tip.GameUlongUID == gameUlongUID);
        return row is null ? new VideoHistoryTip(0, 0) : new VideoHistoryTip(row.DelVideoGuideTip, row.VideoDelByVersion);
    }

    public async Task SaveTipAsync(ulong gameUlongUID, VideoHistoryTip tip)
    {
        await using var database = new DataConnection(options);
        await database.InsertOrReplaceAsync(new RoleVideoHistoryTip
        {
            GameUlongUID = gameUlongUID,
            DelVideoGuideTip = tip.DelVideoGuideTip,
            VideoDelByVersion = tip.VideoDelByVersion,
        });
    }
}
