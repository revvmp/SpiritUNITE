using Microsoft.Extensions.Logging;
using SpiritUNITE.Network.Gcp;
using SpiritUNITE.Protocol;

namespace SpiritUNITE.Network;

public sealed class PacketLog(ILogger logger)
{
    public const string DirectionProperty = "Direction";
    public const string Inbound = "[In] ";
    public const string Outbound = "[Out]";

    private const string TransportSource = "[Gcp] ";
    private const string GameSource = "[Game]";

    public void Received(GcpHead head) => Write(Inbound, head);

    public void Sent(GcpHead head) => Write(Outbound, head);

    public void ReceivedGame(GamePacket packet) => WriteGame(Inbound, packet);

    public void SentGame(GamePacket packet) => WriteGame(Outbound, packet);

    private void Write(string direction, GcpHead head)
    {
        if (!logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        logger.LogDebug(
            "{Source}    |   {Direction}   | {Packet}, {Operation}/0x{Hex:X4}\t| {Size} bytes, seq {Sequence}",
            TransportSource, direction, NameOf(head.Command), head.Command, head.Command, head.PacketLength, head.Sequence);
    }

    private void WriteGame(string direction, GamePacket packet)
    {
        logger.LogDebug(
            "{Source}    |   {Direction}   | Message, {Operation}/0x{Hex:X4}\t| {Size} bytes, uid {Uid}",
            GameSource, direction, packet.MessageId, packet.MessageId, packet.Payload.Length, packet.Uid);
    }

    private static string NameOf(ushort command) =>
        Enum.IsDefined((GcpCommand)command) ? ((GcpCommand)command).ToString() : "Unknown";
}
