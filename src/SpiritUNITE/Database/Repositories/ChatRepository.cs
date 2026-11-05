using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed class ChatRepository(DataOptions options)
{
    public async Task SaveOfflineAsync(RoleChatOffline chat, int maxOfflineNum)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.InsertAsync(chat);
        var overflow = await database.GetTable<RoleChatOffline>()
            .Where(saved => saved.DestUid == chat.DestUid)
            .OrderByDescending(saved => saved.ChatId)
            .Skip(maxOfflineNum)
            .Select(saved => saved.ChatId)
            .ToArrayAsync();
        if (overflow.Length > 0)
        {
            await database.GetTable<RoleChatOffline>()
                .Where(saved => overflow.Contains(saved.ChatId))
                .DeleteAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<RoleChatOffline[]> TakeOfflineAsync(ulong destUid)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var chats = await database.GetTable<RoleChatOffline>()
            .Where(chat => chat.DestUid == destUid)
            .OrderBy(chat => chat.ChatId)
            .ToArrayAsync();
        if (chats.Length > 0)
        {
            var lastChatId = chats[^1].ChatId;
            await database.GetTable<RoleChatOffline>()
                .Where(chat => chat.DestUid == destUid && chat.ChatId <= lastChatId)
                .DeleteAsync();
        }

        await transaction.CommitAsync();
        return chats;
    }

    public async Task<int> DeleteOfflineAsync(ulong destUid, ulong fromUid)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleChatOffline>()
            .Where(chat => chat.DestUid == destUid && chat.FromUid == fromUid)
            .DeleteAsync();
    }
}
