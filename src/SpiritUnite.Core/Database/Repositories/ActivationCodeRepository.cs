using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public sealed record DrawnActivationCode(string ActivationCode, ulong GameUlongUID, long GetTime, bool IsUsed);

public sealed class ActivationCodeRepository(DataOptions options)
{
    public async Task<DrawnActivationCode?> FindDrawnByOwnerAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleActivationCode>().FirstOrDefaultAsync(code => code.GameUlongUID == gameUlongUID);
        return row is null ? null : await ToDrawnAsync(database, row);
    }

    public async Task<DrawnActivationCode?> FindDrawnAsync(string activationCode)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleActivationCode>().FirstOrDefaultAsync(code => code.ActivationCode == activationCode);
        return row is null ? null : await ToDrawnAsync(database, row);
    }

    public async Task<bool> TryInsertDrawnAsync(ulong gameUlongUID, string activationCode, long getTime)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(new RoleActivationCode
            {
                ActivationCode = activationCode,
                GameUlongUID = gameUlongUID,
                GetTime = getTime,
            });
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    public async Task<int> CountUsesAsync(string activationCode)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<ActivationCodeUse>().CountAsync(use => use.ActivationCode == activationCode);
    }

    public async Task<Dictionary<string, int>> CountUsesByCodeAsync()
    {
        await using var database = new DataConnection(options);
        var counts = await database.GetTable<ActivationCodeUse>()
            .GroupBy(use => use.ActivationCode)
            .Select(uses => new { ActivationCode = uses.Key, UseCnt = uses.Count() })
            .ToArrayAsync();
        return counts.ToDictionary(count => count.ActivationCode, count => count.UseCnt, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> HasUsedAsync(string activationCode, ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<ActivationCodeUse>()
            .AnyAsync(use => use.ActivationCode == activationCode && use.GameUlongUID == gameUlongUID);
    }

    public async Task<bool> TryInsertUseAsync(string activationCode, ulong gameUlongUID, long useTime)
    {
        await using var database = new DataConnection(options);
        try
        {
            await database.InsertAsync(new ActivationCodeUse
            {
                ActivationCode = activationCode,
                GameUlongUID = gameUlongUID,
                UseTime = useTime,
            });
            return true;
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }

    private static async Task<DrawnActivationCode> ToDrawnAsync(DataConnection database, RoleActivationCode row) => new(
        row.ActivationCode,
        row.GameUlongUID,
        row.GetTime,
        await database.GetTable<ActivationCodeUse>().AnyAsync(use => use.ActivationCode == row.ActivationCode));
}
