using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed class ServerContentRepository(DataOptions options)
{
    public async Task<MsdkNotice[]> ListNoticesAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<MsdkNotice>().OrderBy(notice => notice.Order).ThenBy(notice => notice.NoticeId).ToArrayAsync();
    }

    public async Task ReplaceNoticesAsync(IReadOnlyList<MsdkNotice> notices)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.GetTable<MsdkNotice>().DeleteAsync();
        foreach (var notice in notices)
        {
            await database.InsertAsync(notice);
        }

        await transaction.CommitAsync();
    }

    public async Task<HostActivationCodeRow[]> ListHostActivationCodesAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<HostActivationCodeRow>().OrderBy(code => code.ActivationCode).ToArrayAsync();
    }

    public async Task<int> CountHostActivationCodesAsync()
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<HostActivationCodeRow>().CountAsync();
    }

    public async Task<bool> TryInsertHostActivationCodeAsync(HostActivationCodeRow code)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(code);
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<bool> DeleteHostActivationCodeAsync(string activationCode)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<HostActivationCodeRow>().Where(code => code.ActivationCode == activationCode).DeleteAsync() > 0;
    }

    public async Task<Dictionary<string, int[]>> LoadListsAsync()
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<ServerList>().ToArrayAsync();
        return rows
            .GroupBy(row => row.ListName, StringComparer.Ordinal)
            .ToDictionary(list => list.Key, list => list.OrderBy(row => row.ListIndex).Select(row => row.ItemId).ToArray(), StringComparer.Ordinal);
    }

    public async Task ReplaceListsAsync(IReadOnlyDictionary<string, int[]> lists)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        foreach (var (listName, itemIds) in lists)
        {
            await database.GetTable<ServerList>().Where(row => row.ListName == listName).DeleteAsync();
            for (var index = 0; index < itemIds.Length; index++)
            {
                await database.InsertAsync(new ServerList { ListName = listName, ListIndex = (ushort)index, ItemId = itemIds[index] });
            }
        }

        await transaction.CommitAsync();
    }
}
