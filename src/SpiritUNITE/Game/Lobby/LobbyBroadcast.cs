using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Settings;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Lobby;

public sealed class LobbyBroadcast(LobbyRoles lobbyRoles, LobbyNotices notices, SettingsManager settings, ILogger<LobbyBroadcast> logger)
{
    public Task<int> PushRefreshAsync() =>
        PushToOnlineAsync("Lobby refresh", async uid => notices.BuildRefreshPackets((await settings.LoadAsync(uid)).CountryCode));

    public Task<int> PushShopNewSaleAsync()
    {
        var packets = new[] { GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidShopNewSaleInfoNtf, notices.BuildShopNewSaleInfoNtf()) };
        return PushToOnlineAsync("Shop new sale", _ => Task.FromResult(packets));
    }

    private async Task<int> PushToOnlineAsync(string pushName, Func<ulong, Task<GamePacket[]>> buildPackets)
    {
        var onlineUids = lobbyRoles.OnlineUids;
        foreach (var uid in onlineUids)
        {
            try
            {
                await lobbyRoles.PushAsync(uid, await buildPackets(uid));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "{PushName} push failed for uid {Uid}", pushName, uid);
            }
        }

        logger.LogInformation("{PushName} pushed to {OnlineCount} online roles", pushName, onlineUids.Count);
        return onlineUids.Count;
    }
}
