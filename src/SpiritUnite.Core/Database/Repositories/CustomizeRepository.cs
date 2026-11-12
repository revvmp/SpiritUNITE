using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Pokemon;

namespace SpiritUnite.Database.Repositories;

public sealed class CustomizeRepository(DataOptions options)
{
    public async Task<IReadOnlyDictionary<int, int>> LoadUsedPersonaliseButtonsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleUsedPersonaliseButton>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(row => row.PasterId, row => row.ButtonID);
    }

    public async Task SetPersonaliseButtonAsync(ulong gameUlongUID, IReadOnlyCollection<int> pasterIds, int buttonID, bool isDefault)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.GetTable<RoleUsedPersonaliseButton>()
            .Where(row => row.GameUlongUID == gameUlongUID && pasterIds.Contains(row.PasterId))
            .DeleteAsync();
        if (!isDefault)
        {
            foreach (var pasterId in pasterIds)
            {
                await database.InsertAsync(new RoleUsedPersonaliseButton
                {
                    GameUlongUID = gameUlongUID,
                    PasterId = pasterId,
                    ButtonID = buttonID,
                });
            }
        }

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<UsedGloryAnnounce>> LoadUsedGloryAnnouncesAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleUsedGloryAnnounce>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .OrderBy(row => row.UsedResID)
            .ToArrayAsync();
        return [.. rows.Select(row => new UsedGloryAnnounce(row.UsedResID, row.UsedResType, row.AnnounceID))];
    }

    public async Task SetGloryAnnounceAsync(ulong gameUlongUID, IReadOnlyCollection<UsedGloryAnnounce> uses, bool isDefault)
    {
        int[] usedResIds = [.. uses.Select(use => use.UsedResID)];
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.GetTable<RoleUsedGloryAnnounce>()
            .Where(row => row.GameUlongUID == gameUlongUID && usedResIds.Contains(row.UsedResID))
            .DeleteAsync();
        if (!isDefault)
        {
            foreach (var use in uses)
            {
                await database.InsertAsync(new RoleUsedGloryAnnounce
                {
                    GameUlongUID = gameUlongUID,
                    UsedResID = use.UsedResID,
                    UsedResType = use.UsedResType,
                    AnnounceID = use.AnnounceID,
                });
            }
        }

        await transaction.CommitAsync();
    }
}
