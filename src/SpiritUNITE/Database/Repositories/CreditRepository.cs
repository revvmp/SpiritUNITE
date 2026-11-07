using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;

namespace SpiritUNITE.Database.Repositories;

public sealed record CreditRecord(int CreditPoint, long UnconfirmedDay, int UnconfirmedCount, long BanEndTime, long ResumeDay, int ResumeCredit);

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

    public async Task SaveAsync(ulong gameUlongUID, CreditRecord record)
    {
        await using var database = new DataConnection(options);
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
    }
}
