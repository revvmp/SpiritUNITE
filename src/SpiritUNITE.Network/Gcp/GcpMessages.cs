using System.Buffers.Binary;

namespace SpiritUNITE.Network.Gcp;

public readonly record struct GcpAuthRequest(ushort AuthType, int TokenLength, ReadOnlyMemory<byte> StartData);

public static class GcpMessages
{
    public const byte ServerKeyMaking = 2;

    private const int AckTrailerMinimumVersion = 5;
    private const int AckTrailerSize = 9;
    private const int AuthResponseRouteIdMinimumVersion = 4;
    private const int AuthResponseBaseSize = 21;
    private const int RouteIdSize = 8;
    private const int BingoBaseSize = 20;
    private const int BingoSessionIdMinimumVersion = 9;
    private const int BingoSettingsMinimumVersion = 8;
    private const int BingoSettingsSize = 5;
    private const int BingoExtendedVersion = 12;
    private const int BingoExtendedSize = 16;
    private const int MaxAppIdLength = 64;
    private const int MaxTokenLength = 4096;
    private const int MaxStartDataLength = 256;
    private const int AuthRequestStartDataVersion = 11;

    public static byte[] BuildAckSection(ReadOnlySpan<byte> sessionKey, int packingVersion)
    {
        var trailerSize = packingVersion >= AckTrailerMinimumVersion ? AckTrailerSize : 0;
        var section = new byte[2 + sessionKey.Length + trailerSize];
        section[0] = ServerKeyMaking;
        section[1] = (byte)sessionKey.Length;
        sessionKey.CopyTo(section.AsSpan(2));
        return section;
    }

    public static byte[] BuildEmptyAuthResponse(int headerVersion) =>
        new byte[AuthResponseBaseSize + (headerVersion >= AuthResponseRouteIdMinimumVersion ? RouteIdSize : 0)];

    public static byte[] BuildEmptyBingo(int headerVersion)
    {
        var size = BingoBaseSize;
        size += headerVersion >= BingoSessionIdMinimumVersion ? RouteIdSize : 0;
        size += headerVersion >= BingoSettingsMinimumVersion ? BingoSettingsSize : 0;
        size += headerVersion == BingoExtendedVersion ? BingoExtendedSize : 0;
        return new byte[size];
    }

    public static GcpAuthRequest ParseAuthRequest(ReadOnlyMemory<byte> body, int headerVersion)
    {
        var offset = 0;
        var authType = ReadUInt16(body.Span, ref offset);
        ReadLengthPrefixed(body, ref offset, MaxAppIdLength);
        var token = ReadLengthPrefixed(body, ref offset, MaxTokenLength);
        var startData = headerVersion >= AuthRequestStartDataVersion
            ? ReadLengthPrefixed(body, ref offset, MaxStartDataLength)
            : ReadOnlyMemory<byte>.Empty;

        return new GcpAuthRequest(authType, token.Length, startData);
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        if (data.Length - offset < sizeof(ushort))
        {
            throw new GcpProtocolException("Auth request ended early");
        }

        var value = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += sizeof(ushort);
        return value;
    }

    private static ReadOnlyMemory<byte> ReadLengthPrefixed(ReadOnlyMemory<byte> data, ref int offset, int maxLength)
    {
        int length = ReadUInt16(data.Span, ref offset);
        if (length > maxLength || data.Length - offset < length)
        {
            throw new GcpProtocolException($"Auth request field length {length} is invalid");
        }

        var field = data.Slice(offset, length);
        offset += length;
        return field;
    }
}
