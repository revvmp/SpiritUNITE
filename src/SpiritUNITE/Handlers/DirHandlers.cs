using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Protocol;

namespace SpiritUNITE.Handlers;

public sealed class DirHandlers(IOptions<LobbyAddressOptions> lobbyAddress, ILogger<DirHandlers> logger)
{
    private const int GameServerAddressRequest = 11002;
    private const int GameServerAddressResponse = 11003;
    private const int ResultInfoField = 1;
    private const int GameServerAddressField = 2;
    private const int AddressUrlField = 1;
    private const int AddressPortField = 2;
    private const int AddressServerIdField = 3;
    private const int ServerId = 1;

    [PacketHandler(Connector.Dir, GameServerAddressRequest)]
    public GamePacket SendLobbyAddress(GamePacket request)
    {
        logger.LogInformation("Sending Lobby address {Host}:{Port} to uid {Uid}",
            lobbyAddress.Value.Host, lobbyAddress.Value.Port, request.Uid);

        return new GamePacket(GameServerAddressResponse, request.Uid, request.ClientSequence, BuildAddressResponse());
    }

    private byte[] BuildAddressResponse()
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteTag(ResultInfoField, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.Empty);
        output.WriteTag(GameServerAddressField, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(BuildAddress());
        output.Flush();
        return buffer.ToArray();
    }

    private ByteString BuildAddress()
    {
        using var buffer = new MemoryStream();
        using var output = new CodedOutputStream(buffer, leaveOpen: true);
        output.WriteTag(AddressUrlField, WireFormat.WireType.LengthDelimited);
        output.WriteString(lobbyAddress.Value.Host);
        output.WriteTag(AddressPortField, WireFormat.WireType.Varint);
        output.WriteInt32(lobbyAddress.Value.Port);
        output.WriteTag(AddressServerIdField, WireFormat.WireType.Varint);
        output.WriteInt32(ServerId);
        output.Flush();
        return ByteString.CopyFrom(buffer.ToArray());
    }
}
