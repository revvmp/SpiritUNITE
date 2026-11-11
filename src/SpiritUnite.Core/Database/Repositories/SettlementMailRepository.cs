using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using LinqToDB.DataProvider.MySql;
using SpiritUnite.Database.Models;

namespace SpiritUnite.Database.Repositories;

public sealed record SettlementMailPlan(long DeliverAfter, RoleMail[] Mails);

public sealed record PendingSettlementMail(SettlementKey Key, SettlementMailPlan Plan);

public sealed class SettlementMailRepository(DataOptions options)
{
    private const string Planned = "credit.mail-plan";
    private const string Delivered = "credit.mail-sent";

    public async Task QueueAsync(SettlementKey key, SettlementMailPlan plan)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await SettlementEffects.RunAsync(database, key, Planned, () => Task.FromResult(plan));
        await transaction.CommitAsync();
    }

    public async Task<PendingSettlementMail[]> FindPendingAsync(long now, CancellationToken cancellationToken)
    {
        await using var database = new DataConnection(options);
        var records = await database.GetTable<SettlementEffectRecord>()
            .Where(row => row.Effect == Planned && !database.GetTable<SettlementEffectRecord>()
                .Any(sent => sent.SettlementID == row.SettlementID && sent.GameUlongUID == row.GameUlongUID && sent.Effect == Delivered))
            .ToArrayAsync(cancellationToken);
        return [.. records.Select(row => new PendingSettlementMail(
                new SettlementKey(row.SettlementID, row.GameUlongUID), SettlementEffects.Deserialize<SettlementMailPlan>(row.Output)))
            .Where(pending => pending.Plan.DeliverAfter <= now)
            .OrderBy(pending => pending.Plan.DeliverAfter)
            .ThenBy(pending => pending.Key.GameUlongUID)];
    }

    public async Task<RoleMail[]> DeliverAsync(PendingSettlementMail pending)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var result = await SettlementEffects.RunAsync(database, pending.Key, Delivered, async () =>
        {
            var saved = new List<RoleMail>();
            foreach (var mail in pending.Plan.Mails.OrderBy(mail => mail.GameUlongUID))
            {
                var recipients = await database.GetTable<Role>()
                    .Where(role => role.GameUlongUID == mail.GameUlongUID)
                    .AsMySql().ForUpdateHint().Select(role => role.GameUlongUID).ToArrayAsync();
                if (recipients.Length == 0)
                {
                    continue;
                }

                saved.Add(await MailRepository.InsertAsync(database, mail));
            }

            return saved.ToArray();
        }, lockRole: false);
        await transaction.CommitAsync();
        return result.Applied ? result.Value : [];
    }
}
