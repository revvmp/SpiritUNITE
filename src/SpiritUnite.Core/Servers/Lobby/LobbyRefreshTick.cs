using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Mail;
using SpiritUnite.Game.RallyChallenge;

namespace SpiritUnite.Servers.Lobby;

public sealed class LobbyRefreshTick(
    LobbyBroadcast broadcast,
    MailRedDotSync mailRedDotSync,
    RallyChallengeManager rallyChallenge,
    TimeProvider clock,
    ILogger<LobbyRefreshTick> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FaultRetryDelay = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(RefreshLoopAsync(stoppingToken), UniteChallengeCloseLoopAsync(stoppingToken), MailRedDotSyncLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Lobby refresh stopped");
        }
    }

    private Task RefreshLoopAsync(CancellationToken stoppingToken) =>
        RepeatAsync("Lobby refresh", async token =>
        {
            await Task.Delay(RefreshInterval, clock, token);
            await broadcast.PushRefreshAsync(token);
        }, stoppingToken);

    private Task MailRedDotSyncLoopAsync(CancellationToken stoppingToken) =>
        RepeatAsync("Mail red dot sync", async token =>
        {
            await Task.Delay(mailRedDotSync.UntilNextSync(), clock, token);
            await mailRedDotSync.PushDueAsync(token);
        }, stoppingToken);

    private Task UniteChallengeCloseLoopAsync(CancellationToken stoppingToken)
    {
        var checkedUntil = clock.GetUtcNow();
        return RepeatAsync("Unite Challenge close", async token =>
        {
            var nextClose = rallyChallenge.FindNextCloseTime(checkedUntil);
            var untilClose = nextClose is { } closeTime ? closeTime - clock.GetUtcNow() : RefreshInterval;
            await Task.Delay(untilClose < TimeSpan.Zero ? TimeSpan.Zero : untilClose < RefreshInterval ? untilClose : RefreshInterval, clock, token);
            var now = clock.GetUtcNow();
            if (nextClose <= now)
            {
                await PushUniteChallengeCloseAsync();
            }

            checkedUntil = now;
        }, stoppingToken);
    }

    private async Task RepeatAsync(string loopName, Func<CancellationToken, Task> iteration, CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await iteration(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "{LoopName} loop iteration failed; the loop continues", loopName);
                await Task.Delay(FaultRetryDelay, clock, stoppingToken);
            }
        }
    }

    private async Task PushUniteChallengeCloseAsync()
    {
        try
        {
            await rallyChallenge.PushCloseNtfAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unite Challenge close push failed");
        }
    }
}
