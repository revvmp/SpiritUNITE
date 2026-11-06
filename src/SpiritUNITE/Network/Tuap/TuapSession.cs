using System.Security.Cryptography;
using System.Text;
using SpiritUNITE.Network.Gcp;

namespace SpiritUNITE.Network.Tuap;

public sealed record TuapService(string Name, GcpCompression Compression);

public sealed class TuapSession(
    Func<string, TuapService?> findService,
    Func<TuapService, ReadOnlyMemory<byte>, Task<byte[]?>> handleRequest,
    Func<byte[], Task> send,
    TimeProvider time) : IDisposable
{
    public const int MaxServiceNameLength = 64;

    private const int MaxAccountLength = 512;
    private const int MaxTokenLength = 4096;
    private const int MaxRouteLength = 256;
    private const int ClientSequenceLength = sizeof(uint);
    private const int MaxPayloadLength = TuapPacket.MaxBodyLength;
    private const byte AesEncryptMethod = 3;
    private const byte NoValue = 0;
    private const byte Compressed = 1;
    private const int BingoSessionIdBytes = 8;
    private const long BingoRouteIdLimit = 1L << 48;
    private const ulong BingoRouteFlag = 1;
    private const uint AckEncryptMethodTag = 1;
    private const uint AckKeyMakingTag = 2;
    private const uint AckKeyTag = 3;
    private const uint AckServerKeyTag = 2;
    private const uint KeyLengthTag = 1;
    private const uint KeyBytesTag = 2;
    private const uint AckCompressModeTag = 4;
    private const uint AckCompressMinSizeTag = 5;
    private const uint AckReservedTag = 6;
    private const uint AckResultTag = 1;
    private const uint AuthRspAccountTag = 1;
    private const uint AuthRspResultTag = 2;
    private const uint AuthRspFirstEmptyTag = 3;
    private const uint AuthRspSecondEmptyTag = 4;
    private const uint BingoSessionTag = 1;
    private const uint BingoCompressModeTag = 2;
    private const uint BingoCompressMinSizeTag = 3;
    private const uint BingoConnectionIdTag = 1;
    private const uint BingoSessionIdTag = 2;
    private const uint BingoRouteIdTag = 3;
    private const uint BingoRouteFlagTag = 4;
    private const uint BingoReservedTag = 5;
    private const uint BingoServiceNameTag = 6;
    private const uint DataAllowLostTag = 2;
    private const uint DataRouteSelectorTag = 3;
    private const uint SynAccountTag = 7;
    private const uint SynRouteTag = 14;
    private const uint RouteServiceTag = 4;
    private const uint ServiceNameTag = 1;
    private const uint AuthTokenTag = 2;
    private const uint AuthServerTimeTag = 4;
    private const uint DataCompressedTag = 1;
    private const uint DataRouteTag = 4;

    private static ReadOnlySpan<byte> SessionKeyAlphabet => "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"u8;

    private static ReadOnlySpan<byte> DataRouteSelector => [0x01, 0x00];

    private TuapService? service;
    private byte[] account = [];
    private GcpCipher? cipher;
    private uint serverSequence;
    private bool authenticated;

    public string? ServiceName => service?.Name;

    public async Task<bool> HandleAsync(TuapHead head, ReadOnlyMemory<byte> body)
    {
        switch (head.Command)
        {
            case GcpCommand.Syn when service is null:
                await AcceptSynAsync(head.Section);
                return true;
            case GcpCommand.AuthReq when cipher is not null && !authenticated:
                await AcceptAuthAsync(body);
                return true;
            case GcpCommand.Data when authenticated:
                return await AnswerDataAsync(head.Section, body);
            case GcpCommand.ClientStop:
                return false;
            default:
                throw new GcpProtocolException($"Unexpected TUAP command {(ushort)head.Command:X4}");
        }
    }

    public void Dispose() => cipher?.Dispose();

    private async Task AcceptSynAsync(TlvField section)
    {
        if (section.Type != TlvType.Bytes)
        {
            throw new GcpProtocolException("TUAP SYN section is not nested");
        }

        var syn = TlvFields.Parse(section.Bytes);
        account = syn.RequireBytes(SynAccountTag, MaxAccountLength).ToArray();
        var name = ReadServiceName(syn.RequireNested(SynRouteTag, MaxRouteLength));
        service = findService(name) ?? throw new GcpProtocolException($"Unknown TUAP service {name}");

        var key = RandomNumberGenerator.GetItems(SessionKeyAlphabet, GcpCipher.KeySize);
        cipher = new GcpCipher(key);
        var serverKey = new TlvWriter().Byte(KeyLengthTag, GcpCipher.KeySize).Bytes(KeyBytesTag, key);
        var ackSection = new TlvWriter().Nested((uint)GcpCommand.Ack, new TlvWriter()
            .Byte(AckEncryptMethodTag, AesEncryptMethod)
            .Byte(AckKeyMakingTag, (byte)KeyMaking.Server)
            .Nested(AckKeyTag, new TlvWriter().Nested(AckServerKeyTag, serverKey))
            .Byte(AckCompressModeTag, service.Compression.Mode)
            .Number(AckCompressMinSizeTag, (ulong)service.Compression.MinSize)
            .Number(AckReservedTag, 0));
        var ackBody = new TlvWriter().Nested((uint)GcpCommand.Ack, new TlvWriter().Byte(AckResultTag, NoValue));
        await SendAsync(GcpCommand.Ack, Now(), ackSection, ackBody);
    }

    private async Task AcceptAuthAsync(ReadOnlyMemory<byte> body)
    {
        var auth = TuapPacket.Unframe(Decrypt(body)).RequireNested((uint)GcpCommand.AuthReq, MaxTokenLength * 2);
        auth.RequireBytes(AuthTokenTag, MaxTokenLength);
        auth.RequireNumber(AuthServerTimeTag);
        authenticated = true;

        var replyBody = new TlvWriter().Nested((uint)GcpCommand.AuthRsp, new TlvWriter()
            .Bytes(AuthRspAccountTag, account)
            .Number(AuthRspResultTag, 0)
            .Bytes(AuthRspFirstEmptyTag, [])
            .Bytes(AuthRspSecondEmptyTag, []));
        await SendAsync(GcpCommand.AuthRsp, Now(), ByteSection(GcpCommand.AuthRsp), replyBody);

        var bingo = new TlvWriter()
            .Number(BingoConnectionIdTag, (ulong)RandomNumberGenerator.GetInt32(int.MaxValue))
            .Bytes(BingoSessionIdTag, Encoding.ASCII.GetBytes(Convert.ToHexString(RandomNumberGenerator.GetBytes(BingoSessionIdBytes))))
            .Number(BingoRouteIdTag, (ulong)Random.Shared.NextInt64(BingoRouteIdLimit))
            .Number(BingoRouteFlagTag, BingoRouteFlag)
            .Byte(BingoReservedTag, NoValue)
            .Bytes(BingoServiceNameTag, Encoding.ASCII.GetBytes(service!.Name));
        var bingoBody = new TlvWriter().Nested((uint)GcpCommand.Bingo, new TlvWriter()
            .Nested(BingoSessionTag, bingo)
            .Byte(BingoCompressModeTag, service.Compression.Mode)
            .Number(BingoCompressMinSizeTag, (ulong)service.Compression.MinSize));
        await SendAsync(GcpCommand.Bingo, Now(), ByteSection(GcpCommand.Bingo), bingoBody);
    }

    private async Task<bool> AnswerDataAsync(TlvField section, ReadOnlyMemory<byte> body)
    {
        if (section.Type != TlvType.Bytes)
        {
            throw new GcpProtocolException("TUAP data section is not nested");
        }

        var data = TlvFields.Parse(section.Bytes);
        if (data.Find(DataRouteTag) is { Type: TlvType.Bytes } route
            && ReadServiceName(TlvFields.Parse(route.Bytes)) != service!.Name)
        {
            throw new GcpProtocolException("TUAP data names a different service than its SYN");
        }

        var plain = Decrypt(body);
        if (plain.Length <= ClientSequenceLength)
        {
            throw new GcpProtocolException("TUAP data body has no payload");
        }

        var payload = plain.AsMemory(ClientSequenceLength);
        var request = data.Find(DataCompressedTag) is { Number: Compressed }
            ? Lz4Block.Decompress(payload.Span, MaxPayloadLength)
            : payload;

        var reply = await handleRequest(service!, request);
        if (reply is null)
        {
            return false;
        }

        var compressed = service!.Compression.Compresses && reply.Length >= service.Compression.MinSize
            ? Lz4Block.Compress(reply)
            : null;
        var useCompressed = compressed is not null && compressed.Length < reply.Length;
        var replySection = new TlvWriter().Nested((uint)GcpCommand.Data, new TlvWriter()
            .Byte(DataCompressedTag, useCompressed ? Compressed : NoValue)
            .Byte(DataAllowLostTag, NoValue)
            .Byte(DataRouteSelectorTag, NoValue)
            .Bytes(DataRouteTag, DataRouteSelector));
        await SendPacketAsync(GcpCommand.Data, Now(), replySection, useCompressed ? compressed! : reply);
        return true;
    }

    private static string ReadServiceName(TlvFields route)
    {
        var name = route.RequireNested(RouteServiceTag, MaxRouteLength).RequireBytes(ServiceNameTag, MaxServiceNameLength).Span;
        if (name.IsEmpty || name.ContainsAnyExceptInRange((byte)0x21, (byte)0x7E))
        {
            throw new GcpProtocolException("TUAP service name is empty or not printable ASCII");
        }

        return Encoding.ASCII.GetString(name);
    }

    private static TlvWriter ByteSection(GcpCommand command) => new TlvWriter().Byte((uint)command, NoValue);

    private byte[] Decrypt(ReadOnlyMemory<byte> body) =>
        cipher is null || body.IsEmpty
            ? throw new GcpProtocolException("TUAP body is empty or arrived before the session key")
            : cipher.Decrypt(body.Span);

    private Task SendAsync(GcpCommand command, long serverTime, TlvWriter section, TlvWriter body) =>
        SendPacketAsync(command, serverTime, section, TuapPacket.Frame(body.Written));

    private Task SendPacketAsync(GcpCommand command, long serverTime, TlvWriter section, ReadOnlySpan<byte> plain)
    {
        var encrypted = cipher!.Encrypt(plain);
        return send(TuapPacket.BuildServer(command, serverTime, ++serverSequence, section, encrypted));
    }

    private long Now() => time.GetUtcNow().ToUnixTimeSeconds();
}
