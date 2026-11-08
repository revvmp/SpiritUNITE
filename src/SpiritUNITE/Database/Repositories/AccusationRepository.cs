using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed class AccusationRepository(DataOptions options)
{
    public async Task AddAsync(IReadOnlyCollection<RoleAccusation> accusations)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        foreach (var accusation in accusations)
        {
            await database.InsertAsync(accusation);
        }

        await transaction.CommitAsync();
    }

    public async Task<bool> ExistsAsync(ulong gameUlongUID, ulong accusedUid, int scene, string battleID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleAccusation>().AnyAsync(saved =>
            saved.GameUlongUID == gameUlongUID
            && saved.AccusedUid == accusedUid
            && saved.Scene == scene
            && saved.BattleID == battleID);
    }
}
