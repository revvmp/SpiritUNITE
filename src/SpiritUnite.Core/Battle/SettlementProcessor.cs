using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database;
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
    private const int MaxSettleAttempts = 10;
    private const int ShutdownJournalAttempts = 2;
    private const int LockRetryBaseDelayMilliseconds = 25;
    private const int LockRetryMaxDelayMilliseconds = 1000;
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RecoveryAge = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PersistenceRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CompletedRetention = TimeSpan.FromDays(7);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<SettlementKey, SettlementDelivery> settling = new();

    public async Task<bool> EnqueueAsync(BattleResult result)
    {
        var key = SettlementKey.Of(result);
        var shutdownAttempts = 0;
        while (true)
        {
            try
            {
                await journal.EnqueueAsync(result);
                return true;
            }
            catch (Exception exception) when (DatabaseFaults.IsTransient(exception))
            {
                if (lifetime.ApplicationStopping.IsCancellationRequested && ++shutdownAttempts >= ShutdownJournalAttempts)
                {
                    logger.LogError(exception, "Settlement {SettlementID} for uid {Uid} was not journaled before shutdown; settling it once without crash recovery",
                        key.SettlementID, key.GameUlongUID);
                    return false;
                }

                logger.LogWarning(exception, "Could not journal settlement {SettlementID} for uid {Uid}; retrying in {Delay} before granting rewards",
                    key.SettlementID, key.GameUlongUID, PersistenceRetryDelay);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Settlement {SettlementID} for uid {Uid} cannot be journaled; settling it once without crash recovery",
                    key.SettlementID, key.GameUlongUID);
                return false;
            }

            await Task.Delay(PersistenceRetryDelay, clock);
        }
    }

    public async Task<SettlementState> ProcessAsync(BattleResult result, bool journaled) =>
        journaled
            ? await ProcessAsync(SettlementKey.Of(result), result)
            : await SettleUnjournaledAsync(result);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextPurge = clock.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RecoverAsync(stoppingToken);
                if (clock.GetUtcNow() >= nextPurge)
                {
                    await PurgeCompletedAsync();
                    nextPurge = clock.GetUtcNow() + PurgeInterval;
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

    private async Task RecoverAsync(CancellationToken stoppingToken)
    {
        var updatedNoLaterThan = (clock.GetUtcNow() - RecoveryAge).ToUnixTimeSeconds();
        foreach (var record in await journal.FindRecoverableAsync(updatedNoLaterThan, stoppingToken))
        {
            stoppingToken.ThrowIfCancellationRequested();
            var key = SettlementKey.Of(record);
            if (settling.TryGetValue(key, out var owner))
            {
                logger.LogInformation("Settlement {SettlementID} for uid {Uid} is {State} since {UpdatedAt} but still being settled here ({Delivery}); recovery leaves it",
                    key.SettlementID, key.GameUlongUID, (SettlementState)record.State, record.UpdatedAt, owner);
                continue;
            }

            logger.LogInformation("Recovering settlement {SettlementID} for uid {Uid} from state {State} after {Attempts} attempts",
                key.SettlementID, key.GameUlongUID, (SettlementState)record.State, record.Attempts);
            await ProcessAsync(key, null);
        }
    }

    private async Task PurgeCompletedAsync()
    {
        var purged = await journal.PurgeCompletedAsync((clock.GetUtcNow() - CompletedRetention).ToUnixTimeSeconds());
        if (purged > 0)
        {
            logger.LogInformation("Purged {Count} settlement journal rows completed more than {Retention} ago", purged, CompletedRetention);
        }
    }

    private async Task<SettlementState> ProcessAsync(SettlementKey key, BattleResult? liveResult)
    {
        var delivery = liveResult is null ? SettlementDelivery.Recovered : SettlementDelivery.Live;
        if (!settling.TryAdd(key, delivery))
        {
            logger.LogWarning("Settlement {SettlementID} for uid {Uid} is already being settled ({Delivery}); skipping this {SkippedDelivery} run",
                key.SettlementID, key.GameUlongUID, settling.GetValueOrDefault(key), delivery);
            return SettlementState.Processing;
        }

        try
        {
            return await ClaimAndSettleAsync(key, liveResult, delivery);
        }
        finally
        {
            settling.TryRemove(key, out _);
        }
    }

    private async Task<SettlementState> ClaimAndSettleAsync(SettlementKey key, BattleResult? liveResult, SettlementDelivery delivery)
    {
        while (true)
        {
            if (await journal.TryClaimAsync(key) is not { } claimed)
            {
                return await CurrentStateAsync(key);
            }

            if (claimed.Attempts > MaxSettleAttempts)
            {
                logger.LogError("Settlement {SettlementID} for uid {Uid} needs review: it was claimed {Attempts} times without completing",
                    key.SettlementID, key.GameUlongUID, claimed.Attempts);
                await journal.FlagForReviewAsync(key, $"Gave up after {MaxSettleAttempts} attempts");
                return await CurrentStateAsync(key);
            }

            var result = liveResult;
            try
            {
                result ??= BattleSettlementSnapshot.Deserialize(claimed.Payload);
                if (SettlementKey.Of(result) != key)
                {
                    throw new InvalidDataException("Settlement identity does not match its snapshot");
                }

                if (result.Human.IsJudge)
                {
                    await settlement.SettleJudgeAsync(result, delivery);
                    await journal.CompleteAsync(key);
                }
                else
                {
                    await settlement.SettleAsync(result, delivery, key);
                }

                return SettlementState.Completed;
            }
            catch (SettlementClaimLostException)
            {
                logger.LogWarning("Settlement {SettlementID} for uid {Uid} was completed or released by another runner; this run granted nothing",
                    key.SettlementID, key.GameUlongUID);
                return await CurrentStateAsync(key);
            }
            catch (Exception exception) when (DatabaseFaults.IsTransient(exception) && claimed.Attempts < MaxSettleAttempts)
            {
                if (await journal.ReleaseForRetryAsync(key, exception.Message))
                {
                    if (result?.Human.IsJudge != true && DatabaseFaults.IsLockConflict(exception)
                        && await WaitForLockRetryAsync(key, claimed.Attempts, delivery))
                    {
                        continue;
                    }

                    logger.LogWarning(exception, "Settlement {SettlementID} for uid {Uid} hit a transient database fault on attempt {Attempts}; journal recovery will retry it",
                        key.SettlementID, key.GameUlongUID, claimed.Attempts);
                }
                else
                {
                    LogFailureAfterCommit(exception, key);
                }

                return await CurrentStateAsync(key);
            }
            catch (Exception exception)
            {
                if (await journal.FlagForReviewAsync(key, exception.Message))
                {
                    logger.LogError(exception, "Settlement {SettlementID} for uid {Uid} needs review after attempt {Attempts}; it will not be replayed automatically",
                        key.SettlementID, key.GameUlongUID, claimed.Attempts);
                }
                else
                {
                    LogFailureAfterCommit(exception, key);
                }

                return await CurrentStateAsync(key);
            }
        }
    }

    private async Task<bool> WaitForLockRetryAsync(SettlementKey key, int attempts, SettlementDelivery delivery)
    {
        if (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            return false;
        }

        var maximumDelay = Math.Min(LockRetryMaxDelayMilliseconds, LockRetryBaseDelayMilliseconds << attempts);
        var delay = TimeSpan.FromMilliseconds(Random.Shared.Next(maximumDelay / 2, maximumDelay + 1));
        logger.LogWarning("Settlement {SettlementID} for uid {Uid} hit a database lock conflict on attempt {Attempts}; retrying in {Delay} with {Delivery} delivery",
            key.SettlementID, key.GameUlongUID, attempts, delay, delivery);
        try
        {
            await Task.Delay(delay, clock, lifetime.ApplicationStopping);
            return true;
        }
        catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
        {
            return false;
        }
    }

    private void LogFailureAfterCommit(Exception exception, SettlementKey key) =>
        logger.LogError(exception, "Settlement {SettlementID} for uid {Uid} failed after its rewards were committed; the steps after the commit did not run",
            key.SettlementID, key.GameUlongUID);

    private async Task<SettlementState> SettleUnjournaledAsync(BattleResult result)
    {
        try
        {
            await (result.Human.IsJudge
                ? settlement.SettleJudgeAsync(result, SettlementDelivery.Live)
                : settlement.SettleAsync(result, SettlementDelivery.Live, null));
            return SettlementState.Completed;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unjournaled settlement {SettlementID} for uid {Uid} failed; its rewards are lost",
                result.Desk.SettlementID.ToString("N"), result.Human.Uid);
            return SettlementState.NeedsReview;
        }
    }

    private async Task<SettlementState> CurrentStateAsync(SettlementKey key) =>
        await journal.FindStateAsync(key) ?? SettlementState.NeedsReview;
}
