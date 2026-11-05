using System.Collections.Concurrent;
using SpiritUNITE.Network;

namespace SpiritUNITE.Servers.Lobby;

public sealed class OffingRestartSessions
{
    private static readonly TimeSpan OffingRestartWindow = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<ulong, DateTimeOffset?> loggedInRoles = new();
    private readonly RoleSessions roleSessions;
    private readonly TimeProvider clock;

    public OffingRestartSessions(RoleSessions roleSessions, TimeProvider clock)
    {
        this.roleSessions = roleSessions;
        this.clock = clock;
        roleSessions.RoleOnline += OnRoleOnlineAsync;
        roleSessions.RoleOffline += OnRoleOfflineAsync;
    }

    public bool CanResume(ulong uid)
    {
        if (roleSessions.IsOnline(uid))
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
