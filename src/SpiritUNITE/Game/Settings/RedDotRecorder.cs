using Microsoft.Extensions.Hosting;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Online;

namespace SpiritUNITE.Game.Settings;

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

    private async Task RecordAsync(ulong gameUlongUID, IReadOnlyList<GamePacket> packets)
    {
        foreach (var packet in packets)
        {
            await RecordAsync(gameUlongUID, packet);
        }
    }

    private Task RecordAsync(ulong gameUlongUID, GamePacket packet) => (PB_ENUM_CS_MSGID_DEF)packet.MsgID switch
    {
        PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf => settings.ReplaceRedDotsAsync(gameUlongUID,
            pbSCRedDotReplaceNtf.Parser.ParseFrom(packet.PkgData).RedDotInfo?.RedDotDetail ?? []),
        PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf => settings.ClearRedDotAsync(gameUlongUID,
            pbSCRedDotClearNtf.Parser.ParseFrom(packet.PkgData).RedDotID),
        PB_ENUM_CS_MSGID_DEF.PbScidBubTipReplaceNtf => settings.ReplaceBubTipsAsync(gameUlongUID,
            pbSCBubTipReplaceNtf.Parser.ParseFrom(packet.PkgData).BubTipInfo?.BubTipDetail ?? []),
        PB_ENUM_CS_MSGID_DEF.PbScidBubTipClearNtf => settings.ClearBubTipAsync(gameUlongUID,
            pbSCBubTipClearNtf.Parser.ParseFrom(packet.PkgData).BubTipID),
        _ => Task.CompletedTask,
    };
}
