using Google.Protobuf;
using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record CouponsFlowEntry(int Num, long Time, int SystemType, pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION ExtraInfo);

public sealed class CouponsFlowRepository(DataOptions options)
{
    public async Task<IReadOnlyList<pbCOMDT_COUPONS_FLOW_INFO>> LoadLatestAsync(ulong gameUlongUID, int maxCount)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleCouponsFlow>()
            .Where(row => row.GameUlongUID == gameUlongUID)
            .OrderByDescending(row => row.FlowID)
            .Take(maxCount)
            .ToArrayAsync();
        return [.. rows.Select(row => new pbCOMDT_COUPONS_FLOW_INFO
        {
            Num = row.Num,
            Time = row.FlowTime,
            SystemType = row.SystemType,
            ExtraInfo = pbCOMDT_COUPONS_FLOW_EXTRA_INFO_UNION.Parser.ParseFrom(row.ExtraInfo),
        })];
    }

    internal static Task AddAsync(DataConnection database, ulong gameUlongUID, CouponsFlowEntry entry) =>
        database.InsertAsync(new RoleCouponsFlow
        {
            GameUlongUID = gameUlongUID,
            Num = entry.Num,
            FlowTime = entry.Time,
            SystemType = entry.SystemType,
            ExtraInfo = entry.ExtraInfo.ToByteArray(),
        });
}
