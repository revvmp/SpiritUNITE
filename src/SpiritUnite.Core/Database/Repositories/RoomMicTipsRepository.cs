using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public sealed class RoomMicTipsRepository(DataOptions options)
{
    public async Task<int> FindShowMicTipsCntAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleMicTips>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .Select(saved => saved.ShowMicTipsCnt)
            .FirstOrDefaultAsync();
    }

    public async Task<int> AddShowMicTipsAsync(ulong gameUlongUID, long now)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleMicTips>().InsertOrUpdateAsync(
            () => new RoleMicTips
            {
                GameUlongUID = gameUlongUID,
                ShowMicTipsTime = now,
                ShowMicTipsCnt = 1,
            },
            saved => new RoleMicTips
            {
                ShowMicTipsTime = now,
                ShowMicTipsCnt = saved.ShowMicTipsCnt + 1,
            });
        return await database.GetTable<RoleMicTips>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .Select(saved => saved.ShowMicTipsCnt)
            .FirstAsync();
    }
}
