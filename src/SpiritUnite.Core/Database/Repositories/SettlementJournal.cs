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

public sealed class SettlementJournal(DataOptions options, TimeProvider clock)
{
    public async Task EnqueueAsync(BattleResult result)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(new BattleSettlementRecord
            {
                SettlementID = result.Desk.SettlementID.ToString("N"),
                GameUlongUID = result.Human.Uid,
                State = (int)SettlementState.Pending,
                Payload = BattleSettlementSnapshot.Serialize(result),
                UpdatedAt = clock.GetUtcNow().ToUnixTimeSeconds(),
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
        }
    }

    public async Task<BattleSettlementRecord> FindAsync(string settlementID, ulong uid)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<BattleSettlementRecord>()
            .FirstAsync(row => row.SettlementID == settlementID && row.GameUlongUID == uid);
    }

    public async Task<BattleSettlementRecord[]> FindPendingAsync(long before, CancellationToken cancellationToken)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<BattleSettlementRecord>()
            .Where(row => row.State == (int)SettlementState.Pending && row.UpdatedAt <= before)
            .OrderBy(row => row.UpdatedAt)
            .Take(32)
            .ToArrayAsync(cancellationToken);
    }

    public async Task<bool> TryBeginAsync(string settlementID, ulong uid)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<BattleSettlementRecord>()
            .Where(row => row.SettlementID == settlementID && row.GameUlongUID == uid && row.State == (int)SettlementState.Pending)
            .Set(row => row.State, (int)SettlementState.Processing)
            .Set(row => row.UpdatedAt, clock.GetUtcNow().ToUnixTimeSeconds())
            .UpdateAsync() == 1;
    }

    public async Task FinishAsync(string settlementID, ulong uid, SettlementState state, string error = "")
    {
        if (state is not (SettlementState.Completed or SettlementState.NeedsReview))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }

        await using var database = new DataConnection(options);
        await database.GetTable<BattleSettlementRecord>()
            .Where(row => row.SettlementID == settlementID && row.GameUlongUID == uid && row.State == (int)SettlementState.Processing)
            .Set(row => row.State, (int)state)
            .Set(row => row.UpdatedAt, clock.GetUtcNow().ToUnixTimeSeconds())
            .Set(row => row.LastError, error.Length <= 2000 ? error : error[..2000])
            .UpdateAsync();
    }
}
