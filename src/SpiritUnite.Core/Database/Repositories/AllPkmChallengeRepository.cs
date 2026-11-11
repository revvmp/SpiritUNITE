using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record AllPkmChallengePkm(int PkmID, int IsPlayed, long WinTime);

public sealed record AllPkmChallengeBattleResult(AllPkmChallengePkm[] Pokemon, pbCOMDT_TASK_INFO[] Tasks);

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
        long now) => (await RecordWinDetailedAsync(gameUlongUID, activityID, pkmID, maxPkmCount, increments, now))?.Pokemon;

    public async Task<AllPkmChallengeBattleResult?> RecordWinDetailedAsync(
        ulong gameUlongUID,
        int activityID,
        int pkmID,
        int maxPkmCount,
        IReadOnlyCollection<TaskProcessIncrement> increments,
        long now,
        SettlementKey? settlement = null,
        SettlementTaskPlan? taskPlan = null)
    {
        if (settlement is not null && settlement.GameUlongUID != gameUlongUID)
        {
            throw new InvalidDataException("Settlement identity does not match event recipient");
        }

        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        async Task<AllPkmChallengeBattleResult?> ApplyAsync()
        {
            if (!await TaskProcessRepository.LockRoleAsync(database, gameUlongUID))
            {
                return null;
            }

            var listed = await ReadAsync(database, gameUlongUID, activityID);
            if (listed.Length >= maxPkmCount || listed.Any(pkm => pkm.PkmID == pkmID))
            {
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
                return null;
            }

            await TaskProcessRepository.ApplyAsync(database, gameUlongUID, increments, now);
            var recorded = await ReadAsync(database, gameUlongUID, activityID);
            return new AllPkmChallengeBattleResult(recorded, taskPlan is null ? [] : await TaskProcessRepository.ReadSettlementTasksAsync(database, gameUlongUID, taskPlan));
        }

        var recorded = settlement is null
            ? await ApplyAsync()
            : (await SettlementEffects.RunAsync(database, settlement, "events.allpokemon", ApplyAsync)).Value;
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
