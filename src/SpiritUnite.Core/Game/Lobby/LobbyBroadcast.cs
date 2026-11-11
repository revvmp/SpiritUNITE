using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Settings;
using SpiritUnite.Online;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Lobby;

public sealed class LobbyBroadcast(LobbyRoles lobbyRoles, LobbyNotices notices, SettingsManager settings, ILogger<LobbyBroadcast> logger)
{
    private const int MaxConcurrentPushes = 16;

    public Task<int> PushRefreshAsync(CancellationToken cancellationToken = default) =>
        PushToOnlineAsync("Lobby refresh", async uid => notices.BuildRefreshPackets((await settings.LoadAsync(uid)).CountryCode), cancellationToken);

    public Task<int> PushShopNewSaleAsync()
    {
        var packets = new[] { GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidShopNewSaleInfoNtf, notices.BuildShopNewSaleInfoNtf()) };
        return PushToOnlineAsync("Shop new sale", _ => Task.FromResult(packets), CancellationToken.None);
    }

    private async Task<int> PushToOnlineAsync(string pushName, Func<ulong, Task<GamePacket[]>> buildPackets, CancellationToken cancellationToken)
    {
        var onlineUids = lobbyRoles.OnlineUids;
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentPushes, CancellationToken = cancellationToken };
        await Parallel.ForEachAsync(onlineUids, parallelOptions, async (uid, token) =>
        {
            try
            {
                var packets = await buildPackets(uid);
                token.ThrowIfCancellationRequested();
                await lobbyRoles.PushAsync(uid, packets);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "{PushName} push failed for uid {Uid}", pushName, uid);
            }
        });

        logger.LogInformation("{PushName} pushed to {OnlineCount} online roles", pushName, onlineUids.Count);
        return onlineUids.Count;
    }
}
