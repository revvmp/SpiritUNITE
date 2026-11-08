using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed class InviteFriendRepository(DataOptions options)
{
    public async Task<ulong?> FindFriendUidAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleInviteFriend>()
            .Where(bind => bind.GameUlongUID == gameUlongUID)
            .Select(bind => bind.FriendUlongUID)
            .ToArrayAsync();
        return rows.Length == 0 ? null : rows[0];
    }

    public async Task<bool> TryBindAsync(ulong gameUlongUID, ulong friendUlongUID, long bindTime)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(new RoleInviteFriend
            {
                GameUlongUID = gameUlongUID,
                FriendUlongUID = friendUlongUID,
                BindTime = bindTime,
            });
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<IReadOnlyDictionary<int, int>> CountMailsAsync(ulong gameUlongUID, IReadOnlyCollection<int> mailIDs)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleMail>()
            .Where(mail => mail.GameUlongUID == gameUlongUID && mailIDs.Contains(mail.MailID))
            .GroupBy(mail => mail.MailID)
            .Select(group => new { MailID = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.MailID, entry => entry.Count);
    }
}
