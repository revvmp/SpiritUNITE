using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Network;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Servers.Lobby;

public sealed class LobbyRefreshTick(
    RoleSessions sessions,
    LobbyNotices notices,
    SettingsManager settings,
    TimeProvider clock,
    ILogger<LobbyRefreshTick> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(RefreshInterval, clock, stoppingToken);
                await RefreshOnlineRolesAsync();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Lobby refresh stopped");
        }
    }

    private async Task<GamePacket[]> BuildRefreshPacketsAsync(ulong uid) =>
        notices.BuildRefreshPackets((await settings.LoadAsync(uid)).CountryCode);

    private async Task RefreshOnlineRolesAsync()
    {
        var onlineUids = sessions.OnlineUids;
        foreach (var uid in onlineUids)
        {
            try
            {
                await sessions.PushAsync(uid, await BuildRefreshPacketsAsync(uid));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Lobby refresh failed for uid {Uid}", uid);
            }
        }

        logger.LogInformation("Lobby refresh pushed to {OnlineCount} online roles", onlineUids.Count);
    }
}
