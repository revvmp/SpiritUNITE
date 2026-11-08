using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Auth;
using SpiritUnite.Network;
using SpiritUnite.Online;
using SpiritUnite.Network.Gcp;
using SpiritUnite.Network.Lwip;
using SpiritUnite.Packets;
using SpiritUnite.Servers.Lobby;
using SpiritUnite.Tools;

namespace SpiritUnite.Servers.Relay;

public sealed class RelayServer(
    IOptions<RelayAddressOptions> relayAddress,
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    BackgroundOperations operations,
    LwipTickets tickets,
    AccountSessionManager sessions,
    RelayRoles relayRoles,
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
        operations,
        logger)
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken);
        relayRoles.Advertise(relayAddress.Value.AdvertisedHost(lobbyAddress.Value), relayAddress.Value.Port);
    }

    protected override GcpSessionHooks SessionHooks { get; } = new()
    {
        Authenticating = relayRoles.ClaimAsync,
        Authenticated = relayRoles.AttachAsync,
        AuthenticatedSessionClosed = relayRoles.DetachAsync,
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
