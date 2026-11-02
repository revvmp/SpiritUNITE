using Microsoft.Extensions.Logging;
using SpiritUNITE.Network.Gcp;

namespace SpiritUNITE.Network;

public sealed class PacketLog(ILogger logger)
{
    public const string DirectionProperty = "Direction";
    public const string Inbound = "[In] ";
    public const string Outbound = "[Out]";

    private const string TransportSource = "[Gcp]";

    public void Received(GcpHead head) => Write(Inbound, head);

    public void Sent(GcpHead head) => Write(Outbound, head);

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

    private static string NameOf(ushort command) =>
        Enum.IsDefined((GcpCommand)command) ? ((GcpCommand)command).ToString() : "Unknown";
}
