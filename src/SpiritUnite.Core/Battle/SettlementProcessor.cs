using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;

namespace SpiritUnite.Battle;

public enum SettlementDelivery
{
    Live,
    Recovered,
}

public sealed class SettlementProcessor(
    SettlementJournal journal,
    BattleSettlement settlement,
    IHostApplicationLifetime lifetime,
    TimeProvider clock,
    ILogger<SettlementProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecoveryAge = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PersistenceRetryDelay = TimeSpan.FromSeconds(5);

    public async Task EnqueueAsync(BattleResult result)
    {
        while (true)
        {
            try
            {
                await journal.EnqueueAsync(result);
                return;
            }
            catch (Exception exception) when (!lifetime.ApplicationStopping.IsCancellationRequested)
            {
                logger.LogError(exception, "Could not persist settlement {SettlementID} for uid {Uid}; retrying before granting rewards",
                    result.Desk.SettlementID, result.Human.Uid);
            }

            await Task.Delay(PersistenceRetryDelay, clock, lifetime.ApplicationStopping);
        }
    }

    public async Task<SettlementState> ProcessAsync(BattleResult result) =>
        await ProcessAsync(await journal.FindAsync(result.Desk.SettlementID.ToString("N"), result.Human.Uid), result);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var before = (clock.GetUtcNow() - RecoveryAge).ToUnixTimeSeconds();
                foreach (var record in await journal.FindPendingAsync(before, stoppingToken))
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    await ProcessAsync(record, null);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Settlement recovery failed");
            }

            try
            {
                await Task.Delay(RecoveryInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<SettlementState> ProcessAsync(BattleSettlementRecord record, BattleResult? liveResult)
    {
        if (!await journal.TryBeginAsync(record.SettlementID, record.GameUlongUID))
        {
            return (SettlementState)(await journal.FindAsync(record.SettlementID, record.GameUlongUID)).State;
        }

        try
        {
            var result = liveResult ?? BattleSettlementSnapshot.Deserialize(record.Payload);
            if (result.Desk.SettlementID.ToString("N") != record.SettlementID || result.Human.Uid != record.GameUlongUID)
            {
                throw new InvalidDataException("Settlement identity does not match its snapshot");
            }

            var delivery = liveResult is null ? SettlementDelivery.Recovered : SettlementDelivery.Live;
            await (result.Human.IsJudge ? settlement.SettleJudgeAsync(result, delivery) : settlement.SettleAsync(result, delivery));
            await journal.FinishAsync(record.SettlementID, record.GameUlongUID, SettlementState.Completed);
            return SettlementState.Completed;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Settlement {SettlementID} for uid {Uid} needs review; it will not be replayed automatically",
                record.SettlementID, record.GameUlongUID);
            await journal.FinishAsync(record.SettlementID, record.GameUlongUID, SettlementState.NeedsReview, exception.Message);
            return (SettlementState)(await journal.FindAsync(record.SettlementID, record.GameUlongUID)).State;
        }
    }
}
