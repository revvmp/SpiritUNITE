using System.Buffers.Binary;
using System.Text;

namespace SpiritUNITE.Network.Gcp;

public readonly record struct GcpAuthReq(ushort AuthType, ReadOnlyMemory<byte> Token, ReadOnlyMemory<byte> TransData);

public static class GcpMessages
{
    private const int AckTrailerMinimumVersion = 5;
    private const int AckTrailerSize = 9;
    private const int AuthRspRouteIdMinimumVersion = 4;
    private const int AuthRspBaseSize = 21;
    private const int RouteIdSize = 8;
    private const int BingoBaseSize = 20;
    private const int BingoSessionIdMinimumVersion = 9;
    private const int BingoSettingsMinimumVersion = 8;
    private const int BingoSettingsSize = 5;
    private const int BingoExtendedVersion = 12;
    private const int BingoExtendedSize = 16;
    private const int MaxAppIdLength = 64;
    private const int MaxTokenSize = 4096;
    private const int MaxTransDataLength = 256;
    private const int AuthReqTransDataVersion = 11;
    private const int DataRouteMinimumVersion = 9;
    private const int DataMergeCountMinimumVersion = 10;
    private const int ClientDataSequenceSize = 4;
    private const int ChannelTokenLengthOffset = 2;
    private const int ChannelTokenLengthSize = 1;
    private const int AckKeyHeaderSize = 2;
    private const int DataCompressedFlagSize = 1;
    private const int DataRouteSize = 2;
    private const int DataMergeCountSize = 1;
    private const byte FirstPrintableAscii = 0x21;
    private const byte LastPrintableAscii = 0x7E;

    public static byte[] BuildAckSection(ReadOnlySpan<byte> sessionKey, int packingVersion)
    {
        var trailerSize = packingVersion >= AckTrailerMinimumVersion ? AckTrailerSize : 0;
        var section = new byte[AckKeyHeaderSize + sessionKey.Length + trailerSize];
        section[0] = (byte)KeyMaking.Server;
        section[1] = (byte)sessionKey.Length;
        sessionKey.CopyTo(section.AsSpan(AckKeyHeaderSize));
        return section;
    }

    public static byte[] BuildEmptyAuthRsp(int headerVersion) =>
        new byte[AuthRspBaseSize + (headerVersion >= AuthRspRouteIdMinimumVersion ? RouteIdSize : 0)];

    public static byte[] BuildEmptyBingo(int headerVersion)
    {
        var size = BingoBaseSize;
        size += headerVersion >= BingoSessionIdMinimumVersion ? RouteIdSize : 0;
        size += headerVersion >= BingoSettingsMinimumVersion ? BingoSettingsSize : 0;
        size += headerVersion == BingoExtendedVersion ? BingoExtendedSize : 0;
        return new byte[size];
    }

    public static byte[] BuildDataSection(int packingVersion)
    {
        var size = DataCompressedFlagSize;
        size += packingVersion >= DataRouteMinimumVersion ? DataRouteSize : 0;
        size += packingVersion >= DataMergeCountMinimumVersion ? DataMergeCountSize : 0;
        return new byte[size];
    }

    public static ReadOnlyMemory<byte> ReadClientData(ReadOnlyMemory<byte> decryptedBody)
    {
        if (decryptedBody.Length < ClientDataSequenceSize)
        {
            throw new GcpProtocolException("Data packet is too short");
        }

        return decryptedBody[ClientDataSequenceSize..];
    }

    public static GcpAuthReq ParseAuthReq(ReadOnlyMemory<byte> body, int headerVersion)
    {
        var offset = 0;
        var authType = ReadUInt16(body.Span, ref offset);
        ReadLengthPrefixed(body, ref offset, MaxAppIdLength);
        var token = ReadLengthPrefixed(body, ref offset, MaxTokenSize);
        var transData = headerVersion >= AuthReqTransDataVersion
            ? ReadLengthPrefixed(body, ref offset, MaxTransDataLength)
            : ReadOnlyMemory<byte>.Empty;

        return new GcpAuthReq(authType, token, transData);
    }

    public static string? TryReadChannelAccessToken(ReadOnlySpan<byte> token)
    {
        if (token.Length <= ChannelTokenLengthOffset)
        {
            return null;
        }

        var accessTokenStart = ChannelTokenLengthOffset + ChannelTokenLengthSize;
        var accessTokenLength = token[ChannelTokenLengthOffset];
        if (accessTokenLength == 0 || token.Length - accessTokenStart < accessTokenLength)
        {
            return null;
        }

        var accessToken = token.Slice(accessTokenStart, accessTokenLength);
        return accessToken.ContainsAnyExceptInRange(FirstPrintableAscii, LastPrintableAscii) ? null : Encoding.ASCII.GetString(accessToken);
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
