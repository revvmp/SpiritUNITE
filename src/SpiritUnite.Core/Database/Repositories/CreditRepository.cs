using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public sealed record CreditRecord(int CreditPoint, long UnconfirmedDay, int UnconfirmedCount, long BanEndTime, long ResumeDay, int ResumeCredit);

public sealed record CreditReduceRecord(
    long GameStartTime,
    int MapMode,
    int MapSubMode,
    int Reason,
    int ReducePoint,
    int HeroID,
    string DeskUniqId);

public sealed class CreditRepository(DataOptions options)
{
    public async Task<CreditRecord?> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var row = await database.GetTable<RoleCredit>().FirstOrDefaultAsync(credit => credit.GameUlongUID == gameUlongUID);
        return row is null
            ? null
            : new CreditRecord(row.CreditPoint, row.UnconfirmedDay, row.UnconfirmedCount, row.BanEndTime, row.ResumeDay, row.ResumeCredit);
    }

    public async Task SaveAsync(ulong gameUlongUID, CreditRecord record, CreditReduceRecord? reduceRecord, int maxReduceRecordNum)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await database.InsertOrReplaceAsync(new RoleCredit
        {
            GameUlongUID = gameUlongUID,
            CreditPoint = record.CreditPoint,
            UnconfirmedDay = record.UnconfirmedDay,
            UnconfirmedCount = record.UnconfirmedCount,
            BanEndTime = record.BanEndTime,
            ResumeDay = record.ResumeDay,
            ResumeCredit = record.ResumeCredit,
        });
        if (reduceRecord is not null)
        {
            await AddReduceRecordAsync(database, gameUlongUID, reduceRecord, maxReduceRecordNum);
        }

        await transaction.CommitAsync();
    }

    public async Task<CreditReduceRecord[]> ListReduceRecordsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleCreditReduceRecord>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .OrderByDescending(saved => saved.CreditReduceRecordId)
            .Select(saved => new CreditReduceRecord(
                saved.GameStartTime,
                saved.MapMode,
                saved.MapSubMode,
                saved.Reason,
                saved.ReducePoint,
                saved.HeroID,
                saved.DeskUniqId))
            .ToArrayAsync();
    }

    public async Task<CreditReduceRecord[]> FindReduceRecordsOfDeskAsync(ulong gameUlongUID, string deskUniqId)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleCreditReduceRecord>()
            .Where(saved => saved.GameUlongUID == gameUlongUID && saved.DeskUniqId == deskUniqId)
            .Select(saved => new CreditReduceRecord(
                saved.GameStartTime,
                saved.MapMode,
                saved.MapSubMode,
                saved.Reason,
                saved.ReducePoint,
                saved.HeroID,
                saved.DeskUniqId))
            .ToArrayAsync();
    }

    private static async Task AddReduceRecordAsync(DataConnection database, ulong gameUlongUID, CreditReduceRecord reduceRecord, int maxReduceRecordNum)
    {
        await database.InsertAsync(new RoleCreditReduceRecord
        {
            GameUlongUID = gameUlongUID,
            GameStartTime = reduceRecord.GameStartTime,
            MapMode = reduceRecord.MapMode,
            MapSubMode = reduceRecord.MapSubMode,
            Reason = reduceRecord.Reason,
            ReducePoint = reduceRecord.ReducePoint,
            HeroID = reduceRecord.HeroID,
            DeskUniqId = reduceRecord.DeskUniqId,
        });
        var overflow = await database.GetTable<RoleCreditReduceRecord>()
            .Where(saved => saved.GameUlongUID == gameUlongUID)
            .OrderByDescending(saved => saved.CreditReduceRecordId)
            .Skip(maxReduceRecordNum)
            .Select(saved => saved.CreditReduceRecordId)
            .ToArrayAsync();
        if (overflow.Length > 0)
        {
            await database.GetTable<RoleCreditReduceRecord>()
                .Where(saved => overflow.Contains(saved.CreditReduceRecordId))
                .DeleteAsync();
        }
    }
}
