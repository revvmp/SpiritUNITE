using Microsoft.Extensions.Hosting;
using SpiritUnite.Packets;
using SpiritUnite.Online;

namespace SpiritUnite.Game.Settings;

public sealed class RedDotRecorder(LobbyRoles lobbyRoles, SettingsManager settings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lobbyRoles.PacketsSending += RecordAsync;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        lobbyRoles.PacketsSending -= RecordAsync;
        return Task.CompletedTask;
    }

    private Task RecordAsync(ulong gameUlongUID, IReadOnlyList<GamePacket> packets) =>
        settings.RecordNotificationsAsync(gameUlongUID, packets);
}
