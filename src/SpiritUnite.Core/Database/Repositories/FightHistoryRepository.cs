using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public sealed record StoredFightHistoryRecord(int MapSubMode, byte[] FightHistoryRecord);

public sealed class FightHistoryRepository(DataOptions options)
{
    public static async Task AddAsync(DataConnection database, RoleFightHistory record, int maxRecordNumPerMapSubMode)
    {
        await database.InsertAsync(record);
        var overflow = await database.GetTable<RoleFightHistory>()
            .Where(saved => saved.GameUlongUID == record.GameUlongUID && saved.MapSubMode == record.MapSubMode)
            .OrderByDescending(saved => saved.FightHistoryId)
            .Skip(maxRecordNumPerMapSubMode)
            .Select(saved => saved.FightHistoryId)
            .ToArrayAsync();
        if (overflow.Length > 0)
        {
            await database.GetTable<RoleFightHistory>()
                .Where(saved => overflow.Contains(saved.FightHistoryId))
                .DeleteAsync();
        }
    }

    public async Task<StoredFightHistoryRecord[]> ListAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleFightHistory>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .OrderBy(saved => saved.FightHistoryId)
            .Select(saved => new StoredFightHistoryRecord(saved.MapSubMode, saved.FightHistoryRecord))
            .ToArrayAsync();
    }

    public async Task<byte[]?> FindBattleReviewInfoAsync(ulong gameUlongUID, int mapSubMode, int fightRecordIndex)
    {
        await using var database = new DataConnection(options);
        var reviews = await database.GetTable<RoleFightHistory>()
            .Where(saved => saved.GameUlongUID == gameUlongUID && saved.MapSubMode == mapSubMode)
            .OrderBy(saved => saved.FightHistoryId)
            .Skip(fightRecordIndex)
            .Take(1)
            .Select(saved => saved.BattleReviewInfo)
            .ToArrayAsync();
        return reviews.FirstOrDefault();
    }
}
