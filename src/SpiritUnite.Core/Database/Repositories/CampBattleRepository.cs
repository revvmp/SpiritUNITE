using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public sealed record CampBattleCampTotal(int Camp, long TotalScore, int MemberNum);

public sealed class CampBattleRepository(DataOptions options)
{
    public async Task<RoleCampBattle?> FindAsync(ulong gameUlongUID, int activityID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleCampBattle>()
            .FirstOrDefaultAsync(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID);
    }

    public async Task<bool> JoinAsync(RoleCampBattle row)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(row);
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<bool> AddScoreAsync(ulong gameUlongUID, int activityID, long score)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleCampBattle>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID)
            .Set(row => row.Score, row => row.Score + score)
            .UpdateAsync() > 0;
    }

    public async Task<CampBattleCampTotal[]> GetCampTotalsAsync(int activityID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleCampBattle>()
            .Where(row => row.ActivityID == activityID)
            .GroupBy(row => row.Camp)
            .Select(group => new CampBattleCampTotal(group.Key, group.Sum(row => row.Score), group.Count()))
            .ToArrayAsync();
    }
}
