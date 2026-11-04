using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Auth;
using SpiritUNITE.Network;
using SpiritUNITE.Network.Gcp;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Servers.Lobby;

public sealed class LobbyServer(
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    AccountSessionManager sessions,
    ILogger<LobbyServer> lobbyLogger,
    ILogger<TcpServer> logger,
    ILogger<Connection> connectionLogger)
    : TcpServer(eNetEventServerType.LobbyServer, lobbyAddress.Value.Port, options, dispatcher, logger, connectionLogger)
{
    protected override GamePacket[] BuildGreeting() =>
        [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidLoginNowNtf, new pbSCLoginNowNtf())];

    protected override async Task<ulong> AuthenticateAsync(GcpAuthReq request)
    {
        var accountId = GcpMessages.TryReadChannelAccessToken(request.Token.Span) is { } accessToken
            ? await sessions.FindAccountIdAsync(accessToken)
            : null;
        if (accountId is null)
        {
            lobbyLogger.LogInformation("Lobby connection has no valid account token");
            return NoAccount;
        }

        lobbyLogger.LogInformation("Lobby connection authenticated as account {AccountId}", accountId);
        return accountId.Value;
    }
}
