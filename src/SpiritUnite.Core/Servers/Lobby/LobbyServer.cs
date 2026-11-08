using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUnite.Auth;
using SpiritUnite.Network;
using SpiritUnite.Online;
using SpiritUnite.Network.Gcp;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Servers.Lobby;

public sealed class LobbyServer(
    IOptions<LobbyAddressOptions> lobbyAddress,
    IOptions<TcpServerOptions> options,
    PacketDispatcher dispatcher,
    BackgroundOperations operations,
    AccountSessionManager sessions,
    LobbyRoles lobbyRoles,
    OffingRestartSessions offingRestarts,
    ILogger<LobbyServer> lobbyLogger,
    ILogger<TcpServer> logger,
    ILogger<Connection> connectionLogger)
    : TcpServer(eNetEventServerType.LobbyServer, lobbyAddress.Value.Port, options, dispatcher, operations, logger, connectionLogger)
{
    protected override GcpSessionHooks SessionHooks { get; } = new()
    {
        Authenticating = lobbyRoles.ClaimAsync,
        AuthenticatedSessionClosed = lobbyRoles.ReleaseAsync,
        LoginCompleted = lobbyRoles.AttachAsync,
        SessionClosed = lobbyRoles.DetachAsync,
        RepliesSending = lobbyRoles.RaiseSendingAsync,
        FindNextSvrPkgSeq = lobbyRoles.FindNextSvrPkgSeq,
    };

    protected override GamePacket[] BuildGreeting(ulong authenticatedUid) =>
        authenticatedUid != NoAccount && offingRestarts.CanResume(authenticatedUid)
            ? [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOffingRestartNtf, new pbSCOffingRestartNtf())]
            : [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidLoginNowNtf, new pbSCLoginNowNtf())];

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
