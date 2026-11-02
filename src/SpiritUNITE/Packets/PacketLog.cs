using Microsoft.Extensions.Logging;
using SpiritUNITE.Network.Gcp;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets;

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

    public void Unhandled(eNetEventServerType serverType, GamePacket packet) =>
        logger.LogInformation("No handler for {Packet}, {Operation} on {ServerType}, {Size} bytes",
            NameOfMessage(packet.MsgID), packet.MsgID, serverType, packet.PkgData.Length);

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
            "{Source}    |   {Direction}   | {Packet}, {Operation}/0x{Hex:X4}\t| {Size} bytes, uid {Uid}",
            GameSource, direction, NameOfMessage(packet.MsgID), packet.MsgID, packet.MsgID, packet.PkgData.Length, packet.Uid);
    }

    private static string NameOfMessage(int msgID) =>
        Enum.IsDefined((PB_ENUM_CS_MSGID_DEF)msgID) ? ((PB_ENUM_CS_MSGID_DEF)msgID).ToString() : "Unknown";

    private static string NameOf(ushort command) =>
        Enum.IsDefined((GcpCommand)command) ? ((GcpCommand)command).ToString() : "Unknown";
}
