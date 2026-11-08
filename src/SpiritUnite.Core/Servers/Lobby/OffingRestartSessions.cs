using System.Collections.Concurrent;
using SpiritUnite.Online;

namespace SpiritUnite.Servers.Lobby;

public sealed class OffingRestartSessions
{
    private static readonly TimeSpan OffingRestartWindow = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<ulong, DateTimeOffset?> loggedInRoles = new();
    private readonly LobbyRoles lobbyRoles;
    private readonly TimeProvider clock;

    public OffingRestartSessions(LobbyRoles lobbyRoles, TimeProvider clock)
    {
        this.lobbyRoles = lobbyRoles;
        this.clock = clock;
        lobbyRoles.RoleOnline += OnRoleOnlineAsync;
        lobbyRoles.RoleOffline += OnRoleOfflineAsync;
    }

    public bool CanResume(ulong uid)
    {
        if (lobbyRoles.IsOnline(uid))
        {
            return true;
        }

        if (!loggedInRoles.TryGetValue(uid, out var offlineSince))
        {
            return false;
        }

        if (offlineSince is null || clock.GetUtcNow() - offlineSince.Value <= OffingRestartWindow)
        {
            return true;
        }

        loggedInRoles.TryRemove(new KeyValuePair<ulong, DateTimeOffset?>(uid, offlineSince));
        return false;
    }

    private Task OnRoleOnlineAsync(ulong uid)
    {
        loggedInRoles[uid] = null;
        return Task.CompletedTask;
    }

    private Task OnRoleOfflineAsync(ulong uid)
    {
        loggedInRoles[uid] = clock.GetUtcNow();
        return Task.CompletedTask;
    }
}
