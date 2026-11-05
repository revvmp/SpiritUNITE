using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SpiritUNITE.Network.Gcp;

public readonly record struct GcpAuthReq(ushort AuthType, ReadOnlyMemory<byte> Token, ReadOnlyMemory<byte> TransData);

public readonly record struct GcpSyn(ReadOnlyMemory<byte> Account, uint? Reserved)
{
    public static readonly GcpSyn Unreadable = new(ReadOnlyMemory<byte>.Empty, null);
}

public sealed record GcpCompression(byte Mode, int MinSize)
{
    public const byte NoCompressMode = 0;
    public const byte Lz4CompressMode = 1;

    public static readonly GcpCompression Lz4 = new(Lz4CompressMode, 100);
    public static readonly GcpCompression None = new(NoCompressMode, 40);

    public bool Compresses => Mode == Lz4CompressMode;
}

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
    private const int AckBodySize = 3;
    private const int SynAccountOffset = 10;
    private const int SynAccountSelectorOffset = sizeof(ushort);
    private const int SynAccountLengthOffset = SynAccountSelectorOffset + sizeof(byte);
    private const int SynAccountHeadSize = SynAccountLengthOffset + sizeof(uint);
    private const byte SynAccountStringSelector = 3;
    private const int MaxSynAccountStringLength = 256;
    private const int SynTransMaskAndReserved2Size = sizeof(uint) + sizeof(uint);
    private const int SynHasRelayInfoOffset = 0;
    private const int SynHasAuthInfoOffset = 1;
    private const int SynRouteFlagOffset = 3;
    private const int SynFlagsSize = 4;
    private const byte SynUnionAbsent = 0;
    private const int AuthRspAfterAccountZeroSize = sizeof(ushort) + sizeof(ushort);
    private const uint AuthRspUnsetValue = uint.MaxValue;
    private const int BingoSessionTokenOffset = sizeof(uint);
    private const int BingoSessionTokenSize = 16;
    private const int ServerStopTconndIdLengthOffset = sizeof(int) + sizeof(int) + sizeof(uint) + sizeof(ushort);
    private const uint EmptyTconndIdLength = 1;
    private const int ServerStopSize = ServerStopTconndIdLengthOffset + sizeof(uint) + (int)EmptyTconndIdLength;
    private const string BingoSessionTokenAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public const byte DataCompressed = 1;
    public const byte DataRouteSelector = 0x10;

    public static byte[] BuildAckSection(ReadOnlySpan<byte> sessionKey, int packingVersion, GcpCompression compression)
    {
        var trailerSize = packingVersion >= AckTrailerMinimumVersion ? AckTrailerSize : 0;
        var section = new byte[AckKeyHeaderSize + sessionKey.Length + trailerSize];
        section[0] = (byte)KeyMaking.Server;
        section[1] = (byte)sessionKey.Length;
        sessionKey.CopyTo(section.AsSpan(AckKeyHeaderSize));
        if (trailerSize > 0)
        {
            WriteCompressSettings(section.AsSpan(AckKeyHeaderSize + sessionKey.Length), compression);
        }

        return section;
    }

    public static byte[] BuildAckBody() => new byte[AckBodySize];

    public static byte[] BuildEmptyAuthRsp(int headerVersion) =>
        new byte[AuthRspBaseSize + (headerVersion >= AuthRspRouteIdMinimumVersion ? RouteIdSize : 0)];

    public static GcpSyn ParseSyn(ReadOnlyMemory<byte> section)
    {
        var span = section.Span;
        if (span.Length < SynAccountOffset + SynAccountHeadSize || span[SynAccountOffset + SynAccountSelectorOffset] != SynAccountStringSelector)
        {
            return GcpSyn.Unreadable;
        }

        var accountStringLength = BinaryPrimitives.ReadUInt32BigEndian(span[(SynAccountOffset + SynAccountLengthOffset)..]);
        var accountStringOffset = SynAccountOffset + SynAccountHeadSize;
        if (accountStringLength > MaxSynAccountStringLength || span.Length - accountStringOffset < accountStringLength)
        {
            return GcpSyn.Unreadable;
        }

        var accountSize = SynAccountHeadSize + (int)accountStringLength;
        var account = section.Slice(SynAccountOffset, accountSize).ToArray();
        var flagsOffset = SynAccountOffset + accountSize + SynTransMaskAndReserved2Size;
        var reservedOffset = flagsOffset + SynFlagsSize;
        if (span.Length < reservedOffset + sizeof(uint)
            || span[flagsOffset + SynHasRelayInfoOffset] != SynUnionAbsent
            || span[flagsOffset + SynHasAuthInfoOffset] != SynUnionAbsent
            || span[flagsOffset + SynRouteFlagOffset] != SynUnionAbsent)
        {
            return new GcpSyn(account, null);
        }

        return new GcpSyn(account, BinaryPrimitives.ReadUInt32BigEndian(span[reservedOffset..]));
    }

    public static byte[] BuildAccountAuthRsp(int headerVersion, ReadOnlySpan<byte> synAccount)
    {
        if (synAccount.IsEmpty)
        {
            return BuildEmptyAuthRsp(headerVersion);
        }

        var routeIdSize = headerVersion >= AuthRspRouteIdMinimumVersion ? RouteIdSize : 0;
        var authRsp = new byte[synAccount.Length + routeIdSize + AuthRspAfterAccountZeroSize + sizeof(uint) + sizeof(ushort) + sizeof(uint) + sizeof(ushort) + sizeof(ushort)];
        synAccount.CopyTo(authRsp);
        var unsetOffset = synAccount.Length + routeIdSize + AuthRspAfterAccountZeroSize;
        BinaryPrimitives.WriteUInt32BigEndian(authRsp.AsSpan(unsetOffset), AuthRspUnsetValue);
        BinaryPrimitives.WriteUInt32BigEndian(authRsp.AsSpan(unsetOffset + sizeof(uint) + sizeof(ushort)), AuthRspUnsetValue);
        return authRsp;
    }

    public static byte[] BuildSessionBingo(int headerVersion, GcpCompression compression)
    {
        var bingo = BuildBingo(headerVersion, compression);
        BinaryPrimitives.WriteUInt32BigEndian(bingo, (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue));
        var token = RandomNumberGenerator.GetString(BingoSessionTokenAlphabet, BingoSessionTokenSize);
        Encoding.ASCII.GetBytes(token, bingo.AsSpan(BingoSessionTokenOffset, BingoSessionTokenSize));
        return bingo;
    }

    public static byte[] BuildBingo(int headerVersion, GcpCompression compression)
    {
        var size = BingoBaseSize;
        size += headerVersion >= BingoSessionIdMinimumVersion ? RouteIdSize : 0;
        size += headerVersion >= BingoSettingsMinimumVersion ? BingoSettingsSize : 0;
        size += headerVersion == BingoExtendedVersion ? BingoExtendedSize : 0;
        var bingo = new byte[size];
        if (headerVersion >= BingoSettingsMinimumVersion)
        {
            var settingsOffset = BingoBaseSize + (headerVersion >= BingoSessionIdMinimumVersion ? RouteIdSize : 0);
            WriteCompressSettings(bingo.AsSpan(settingsOffset), compression);
        }

        return bingo;
    }

    public static byte[] BuildServerStop(GcpStopReason reason, int exCode)
    {
        var stop = new byte[ServerStopSize];
        BinaryPrimitives.WriteInt32BigEndian(stop, (int)reason);
        BinaryPrimitives.WriteInt32BigEndian(stop.AsSpan(sizeof(int)), exCode);
        BinaryPrimitives.WriteUInt32BigEndian(stop.AsSpan(ServerStopTconndIdLengthOffset), EmptyTconndIdLength);
        return stop;
    }

    public static byte[] BuildDataSection(int packingVersion, bool compressed, byte sessionRoute)
    {
        var section = new byte[DataSectionSize(packingVersion)];
        section[0] = compressed ? DataCompressed : (byte)0;
        if (packingVersion >= DataRouteMinimumVersion)
        {
            section[DataCompressedFlagSize] = DataRouteSelector;
            section[DataCompressedFlagSize + 1] = sessionRoute;
        }

        return section;
    }

    public static bool IsCompressedData(ReadOnlySpan<byte> section) => !section.IsEmpty && section[0] == DataCompressed;

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

    private static int DataSectionSize(int packingVersion)
    {
        var size = DataCompressedFlagSize;
        size += packingVersion >= DataRouteMinimumVersion ? DataRouteSize : 0;
        size += packingVersion >= DataMergeCountMinimumVersion ? DataMergeCountSize : 0;
        return size;
    }

    private static void WriteCompressSettings(Span<byte> destination, GcpCompression compression)
    {
        destination[0] = compression.Mode;
        BinaryPrimitives.WriteUInt32BigEndian(destination[1..], (uint)compression.MinSize);
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
