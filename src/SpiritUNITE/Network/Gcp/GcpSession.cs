using System.Security.Cryptography;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Network.Gcp;

public sealed class GcpSession(
    Connector connector,
    GamePacket[] greeting,
    PacketDispatcher dispatcher,
    ILogger logger) : IDisposable
{
    private const int MaxLoggedBytes = 64;

    private readonly PacketLog packetLog = new(logger);
    private GcpCipher? cipher;
    private GcpSessionState state = GcpSessionState.AwaitingSyn;
    private uint nextSequence;

    private enum GcpSessionState
    {
        AwaitingSyn,
        AwaitingAuth,
        Established,
    }

    public IReadOnlyList<byte[]> Handle(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        packetLog.Received(head);

        return (state, (GcpCommand)head.Command) switch
        {
            (GcpSessionState.AwaitingSyn, GcpCommand.Syn) => AcceptSyn(head, packet),
            (GcpSessionState.AwaitingAuth, GcpCommand.AuthRequest) => AcceptAuth(head, packet),
            (GcpSessionState.Established, GcpCommand.Data) => AcceptData(head, packet),
            (GcpSessionState.Established, _) => IgnoreEstablishedPacket(head),
            _ => throw new GcpProtocolException($"Command 0x{head.Command:X4} is not valid in state {state}"),
        };
    }

    public void Dispose() => cipher?.Dispose();

    private byte[][] AcceptSyn(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        var section = packet[GcpHead.Size..(int)head.HeadLength];
        if (section.IsEmpty || section.Span[0] != GcpMessages.ServerKeyMaking)
        {
            throw new GcpProtocolException("SYN does not request a server generated key");
        }

        var sessionKey = RandomNumberGenerator.GetBytes(GcpCipher.KeySize);
        cipher = new GcpCipher(sessionKey);
        state = GcpSessionState.AwaitingAuth;

        return [BuildPacket(head, GcpCommand.Ack, GcpMessages.BuildAckSection(sessionKey, head.PackingVersion), [])];
    }

    private byte[][] AcceptAuth(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        var body = cipher!.Decrypt(packet.Span[(int)head.HeadLength..]);
        var request = GcpMessages.ParseAuthRequest(body, head.HeaderVersion);
        logger.LogInformation("GCP auth type {AuthType}, token {TokenLength} bytes, start data {StartDataLength} bytes",
            request.AuthType, request.TokenLength, request.StartData.Length);
        LogStartData(request.StartData.Span);

        state = GcpSessionState.Established;
        List<byte[]> replies =
        [
            BuildPacket(head, GcpCommand.AuthResponse, [], GcpMessages.BuildEmptyAuthResponse(head.HeaderVersion)),
            BuildPacket(head, GcpCommand.Bingo, [], GcpMessages.BuildEmptyBingo(head.HeaderVersion)),
        ];

        replies.AddRange(greeting.Select(packet => BuildGamePacket(head, packet)));

        if (!request.StartData.IsEmpty)
        {
            replies.AddRange(DispatchGamePacket(head, request.StartData));
        }

        return [.. replies];
    }

    private byte[][] AcceptData(GcpHead head, ReadOnlyMemory<byte> packet)
    {
        var body = cipher!.Decrypt(packet.Span[(int)head.HeadLength..]);
        return DispatchGamePacket(head, GcpMessages.ReadClientData(body));
    }

    private byte[][] DispatchGamePacket(GcpHead head, ReadOnlyMemory<byte> data)
    {
        var request = ParseGamePacket(data);
        packetLog.ReceivedGame(request);

        try
        {
            if (dispatcher.TryDispatch(connector, request, out var replies))
            {
                return [.. replies.Select(reply => BuildGamePacket(head, reply))];
            }
        }
        catch (InvalidProtocolBufferException exception)
        {
            throw new GcpProtocolException($"Message {request.MessageId} is not valid protobuf: {exception.Message}");
        }

        logger.LogInformation("No handler for message {MessageId} on {Connector}", request.MessageId, connector);
        return [];
    }

    private byte[] BuildGamePacket(GcpHead head, GamePacket packet)
    {
        packetLog.SentGame(packet);
        return BuildPacket(head, GcpCommand.Data, GcpMessages.BuildDataSection(head.PackingVersion), packet.ToByteArray());
    }

    private static GamePacket ParseGamePacket(ReadOnlyMemory<byte> data)
    {
        try
        {
            return GamePacket.Parse(data);
        }
        catch (InvalidDataException exception)
        {
            throw new GcpProtocolException(exception.Message);
        }
    }

    private byte[][] IgnoreEstablishedPacket(GcpHead head)
    {
        logger.LogInformation("Ignoring command 0x{Command:X4}: nothing handles it yet", head.Command);
        return [];
    }

    private void LogStartData(ReadOnlySpan<byte> startData)
    {
        var loggedBytes = startData[..Math.Min(startData.Length, MaxLoggedBytes)];
        logger.LogDebug("Start data begins {Hex}", Convert.ToHexString(loggedBytes));
    }

    private byte[] BuildPacket(GcpHead request, GcpCommand command, ReadOnlySpan<byte> section, ReadOnlySpan<byte> body)
    {
        var headLength = GcpHead.Size + section.Length;
        var packet = new byte[headLength + body.Length];
        var head = new GcpHead(
            request.PackingVersion,
            request.HeaderVersion,
            (ushort)command,
            IsEncrypted: false,
            nextSequence++,
            (uint)headLength,
            (uint)body.Length);

        head.WriteTo(packet);
        section.CopyTo(packet.AsSpan(GcpHead.Size));
        body.CopyTo(packet.AsSpan(headLength));
        packetLog.Sent(head);
        return packet;
    }
}
