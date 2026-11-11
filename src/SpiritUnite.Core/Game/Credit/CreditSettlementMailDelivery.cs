using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Mail;

namespace SpiritUnite.Game.Credit;

public sealed class CreditSettlementMailDelivery(
    SettlementMailRepository repository,
    MailManager mail,
    TimeProvider clock,
    ILogger<CreditSettlementMailDelivery> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Settlement punishment mail delivery failed; pending mail will retry");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task DeliverPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await repository.FindPendingAsync(clock.GetUtcNow().ToUnixTimeSeconds(), cancellationToken);
        foreach (var battle in pending.GroupBy(item => item.Key.SettlementID))
        {
            var saved = new List<RoleMail>();
            foreach (var item in battle)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    saved.AddRange(await repository.DeliverAsync(item));
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Settlement {SettlementID} punishment mail for uid {Uid} remains pending",
                        item.Key.SettlementID, item.Key.GameUlongUID);
                }
            }

            foreach (var receiver in saved.GroupBy(item => (item.GameUlongUID, item.MailType)))
            {
                try
                {
                    await mail.PushSystemMailBatchAsync(receiver.Key.GameUlongUID, receiver.Key.MailType, [.. receiver]);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Uid {Uid} has persisted punishment mail but its live notification failed", receiver.Key.GameUlongUID);
                }
            }
        }
    }
}
