using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Tasks;

namespace SpiritUNITE.Database.Repositories;

public sealed record AllPkmChallengePkm(int PkmID, int IsPlayed, long WinTime);

public sealed class AllPkmChallengeRepository(DataOptions options)
{
    private const int Played = 1;

    public async Task<AllPkmChallengePkm[]> FindAsync(ulong gameUlongUID, int activityID)
    {
        await using var database = new DataConnection(options);
        return await ReadAsync(database, gameUlongUID, activityID);
    }

    public async Task<AllPkmChallengePkm[]?> RecordWinAsync(
        ulong gameUlongUID,
        int activityID,
        int pkmID,
        int maxPkmCount,
        IReadOnlyCollection<TaskProcessIncrement> increments,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TaskProcessRepository.LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return null;
        }

        var listed = await ReadAsync(database, gameUlongUID, activityID);
        if (listed.Length >= maxPkmCount || listed.Any(pkm => pkm.PkmID == pkmID))
        {
            await transaction.RollbackAsync();
            return null;
        }

        try
        {
            await database.InsertAsync(new RoleAllPkmChallenge
            {
                GameUlongUID = gameUlongUID,
                ActivityID = activityID,
                PkmID = pkmID,
                WinTime = now,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return null;
        }

        await TaskProcessRepository.ApplyAsync(database, gameUlongUID, increments, now);
        var recorded = await ReadAsync(database, gameUlongUID, activityID);
        await transaction.CommitAsync();
        return recorded;
    }

    public async Task MarkPlayedAsync(ulong gameUlongUID, int activityID, IReadOnlyCollection<int> pkmIDs)
    {
        await using var database = new DataConnection(options);
        await database.GetTable<RoleAllPkmChallenge>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID && pkmIDs.Contains(row.PkmID))
            .Set(row => row.IsPlayed, Played)
            .UpdateAsync();
    }

    private static async Task<AllPkmChallengePkm[]> ReadAsync(DataConnection database, ulong gameUlongUID, int activityID) =>
        await database.GetTable<RoleAllPkmChallenge>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID)
            .OrderBy(row => row.WinTime)
            .ThenBy(row => row.PkmID)
            .Select(row => new AllPkmChallengePkm(row.PkmID, row.IsPlayed, row.WinTime))
            .ToArrayAsync();
}
