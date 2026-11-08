using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed class FabulousRepository(DataOptions options)
{
    public async Task<int> AddBeFabulousAsync(ulong gameUlongUID, long now)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleBeFabulous>().InsertOrUpdateAsync(
            () => new RoleBeFabulous
            {
                GameUlongUID = gameUlongUID,
                BeFabulousCnt = 1,
                BeFblsCntLastUpdTime = now,
            },
            saved => new RoleBeFabulous
            {
                BeFabulousCnt = saved.BeFabulousCnt + 1,
                BeFblsCntLastUpdTime = now,
            });
        return await database.GetTable<RoleBeFabulous>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .Select(saved => saved.BeFabulousCnt)
            .FirstAsync();
    }

    public async Task<RoleBeFabulous?> FindBeFabulousAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleBeFabulous>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .FirstOrDefaultAsync();
    }

    public async Task<int> FindBeFabulousCntAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleBeFabulous>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .Select(saved => saved.BeFabulousCnt)
            .FirstOrDefaultAsync();
    }
}
