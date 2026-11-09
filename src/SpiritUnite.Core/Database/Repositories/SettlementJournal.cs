using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Battle;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public enum SettlementState
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    NeedsReview = 3,
}

public sealed record SettlementKey(string SettlementID, ulong GameUlongUID)
{
    public static SettlementKey Of(BattleResult result) => new(result.Desk.SettlementID.ToString("N"), result.Human.Uid);

    public static SettlementKey Of(BattleSettlementRecord record) => new(record.SettlementID, record.GameUlongUID);
}

public sealed class SettlementClaimLostException(SettlementKey key)
    : Exception($"Settlement {key.SettlementID} for uid {key.GameUlongUID} is no longer claimed for processing");

public sealed class SettlementJournal(DataOptions options, TimeProvider clock)
{
    private const int MaxErrorLength = 2000;
    private const int RecoveryBatchSize = 32;

    public async Task EnqueueAsync(BattleResult result)
    {
        var key = SettlementKey.Of(result);
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(new BattleSettlementRecord
            {
                SettlementID = key.SettlementID,
                GameUlongUID = key.GameUlongUID,
                State = (int)SettlementState.Pending,
                Payload = BattleSettlementSnapshot.Serialize(result),
                UpdatedAt = clock.GetUtcNow().ToUnixTimeSeconds(),
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
        }
    }

    public async Task<SettlementState?> FindStateAsync(SettlementKey key)
    {
        await using var database = new DataConnection(options);
        var states = await Find(database, key).Select(row => row.State).ToArrayAsync();
        return states.Length == 0 ? null : (SettlementState)states[0];
    }

    public async Task<BattleSettlementRecord[]> FindRecoverableAsync(long updatedNoLaterThan, CancellationToken cancellationToken)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<BattleSettlementRecord>()
            .Where(row => (row.State == (int)SettlementState.Pending || row.State == (int)SettlementState.Processing)
                && row.UpdatedAt <= updatedNoLaterThan)
            .OrderBy(row => row.UpdatedAt)
            .Take(RecoveryBatchSize)
            .Select(row => new BattleSettlementRecord
            {
                SettlementID = row.SettlementID,
                GameUlongUID = row.GameUlongUID,
                State = row.State,
                Attempts = row.Attempts,
                UpdatedAt = row.UpdatedAt,
            })
            .ToArrayAsync(cancellationToken);
    }

    public async Task<BattleSettlementRecord?> TryClaimAsync(SettlementKey key)
    {
        await using var database = new DataConnection(options);
        var claimed = await Find(database, key)
            .Where(row => row.State == (int)SettlementState.Pending || row.State == (int)SettlementState.Processing)
            .Set(row => row.State, (int)SettlementState.Processing)
            .Set(row => row.Attempts, row => row.Attempts + 1)
            .Set(row => row.UpdatedAt, clock.GetUtcNow().ToUnixTimeSeconds())
            .UpdateAsync();
        return claimed == 1 ? await Find(database, key).FirstAsync() : null;
    }

    public static async Task<bool> CompleteAsync(DataConnection database, SettlementKey key, long now) =>
        await Find(database, key)
            .Where(row => row.State == (int)SettlementState.Processing)
            .Set(row => row.State, (int)SettlementState.Completed)
            .Set(row => row.Payload, Array.Empty<byte>())
            .Set(row => row.LastError, string.Empty)
            .Set(row => row.UpdatedAt, now)
            .UpdateAsync() == 1;

    public async Task<bool> CompleteAsync(SettlementKey key)
    {
        await using var database = new DataConnection(options);
        return await CompleteAsync(database, key, clock.GetUtcNow().ToUnixTimeSeconds());
    }

    public Task<bool> ReleaseForRetryAsync(SettlementKey key, string error) =>
        LeaveProcessingAsync(key, SettlementState.Pending, error);

    public Task<bool> FlagForReviewAsync(SettlementKey key, string error) =>
        LeaveProcessingAsync(key, SettlementState.NeedsReview, error);

    public async Task<int> PurgeCompletedAsync(long completedNoLaterThan)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<BattleSettlementRecord>()
            .Where(row => row.State == (int)SettlementState.Completed && row.UpdatedAt <= completedNoLaterThan)
            .DeleteAsync();
    }

    private async Task<bool> LeaveProcessingAsync(SettlementKey key, SettlementState state, string error)
    {
        await using var database = new DataConnection(options);
        return await Find(database, key)
            .Where(row => row.State == (int)SettlementState.Processing)
            .Set(row => row.State, (int)state)
            .Set(row => row.UpdatedAt, clock.GetUtcNow().ToUnixTimeSeconds())
            .Set(row => row.LastError, error.Length <= MaxErrorLength ? error : error[..MaxErrorLength])
            .UpdateAsync() == 1;
    }

    private static IQueryable<BattleSettlementRecord> Find(DataConnection database, SettlementKey key) =>
        database.GetTable<BattleSettlementRecord>()
            .Where(row => row.SettlementID == key.SettlementID && row.GameUlongUID == key.GameUlongUID);
}
