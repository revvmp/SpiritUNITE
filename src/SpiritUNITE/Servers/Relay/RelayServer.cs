using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Auth;
using SpiritUNITE.Network;
using SpiritUNITE.Network.Gcp;
using SpiritUNITE.Network.Lwip;
using SpiritUNITE.Packets;
using SpiritUNITE.Servers.Lobby;

namespace SpiritUNITE.Servers.Relay;

public sealed class RelayServer(
    IOptions<RelayAddressOptions> relayAddress,
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    LwipTickets tickets,
    AccountSessionManager sessions,
    RelaySessions relaySessions,
    ILogger<RelayServer> relayLogger,
    ILogger<LwipServer> logger)
    : LwipServer(
        eNetEventServerType.RelayServer,
        relayAddress.Value.AdvertisedHost(lobbyAddress.Value),
        relayAddress.Value.Port,
        GcpCompression.None,
        options,
        dispatcher,
        tickets,
        logger)
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken);
        relaySessions.Advertise(relayAddress.Value.AdvertisedHost(lobbyAddress.Value), relayAddress.Value.Port);
    }

    protected override GcpSessionHooks SessionHooks { get; } = new()
    {
        Authenticated = relaySessions.AttachAsync,
        AuthenticatedSessionClosed = relaySessions.DetachAsync,
    };

    protected override async Task<ulong> AuthenticateAsync(GcpAuthReq request)
    {
        var accountId = GcpMessages.TryReadChannelAccessToken(request.Token.Span) is { } accessToken
            ? await sessions.FindAccountIdAsync(accessToken)
            : null;
        if (accountId is null)
        {
            relayLogger.LogInformation("Relay connection has no valid account token");
            return NoAccount;
        }

        relayLogger.LogInformation("Relay connection authenticated as account {AccountId}", accountId);
        return accountId.Value;
    }
}
