using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.RallyChallenge;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Servers.Lobby;

public sealed class LobbyRefreshTick(
    LobbyRoles lobbyRoles,
    LobbyNotices notices,
    SettingsManager settings,
    RallyChallengeManager rallyChallenge,
    TimeProvider clock,
    ILogger<LobbyRefreshTick> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.WhenAll(RefreshLoopAsync(stoppingToken), UniteChallengeCloseLoopAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Lobby refresh stopped");
        }
    }

    private async Task RefreshLoopAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            await Task.Delay(RefreshInterval, clock, stoppingToken);
            await RefreshOnlineRolesAsync();
        }
    }

    private async Task UniteChallengeCloseLoopAsync(CancellationToken stoppingToken)
    {
        var checkedUntil = clock.GetUtcNow();
        while (true)
        {
            var nextClose = rallyChallenge.FindNextCloseTime(checkedUntil);
            var untilClose = nextClose is { } closeTime ? closeTime - clock.GetUtcNow() : RefreshInterval;
            await Task.Delay(untilClose < TimeSpan.Zero ? TimeSpan.Zero : untilClose < RefreshInterval ? untilClose : RefreshInterval, clock, stoppingToken);
            var now = clock.GetUtcNow();
            if (nextClose <= now)
            {
                await PushUniteChallengeCloseAsync();
            }

            checkedUntil = now;
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

    private async Task<GamePacket[]> BuildRefreshPacketsAsync(ulong uid) =>
        notices.BuildRefreshPackets((await settings.LoadAsync(uid)).CountryCode);

    private async Task RefreshOnlineRolesAsync()
    {
        var onlineUids = lobbyRoles.OnlineUids;
        foreach (var uid in onlineUids)
        {
            try
            {
                await lobbyRoles.PushAsync(uid, await BuildRefreshPacketsAsync(uid));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Lobby refresh failed for uid {Uid}", uid);
            }
        }

        logger.LogInformation("Lobby refresh pushed to {OnlineCount} online roles", onlineUids.Count);
    }
}
