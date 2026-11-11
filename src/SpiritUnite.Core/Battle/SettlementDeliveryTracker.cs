using System.Collections.Concurrent;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Online;

namespace SpiritUnite.Battle;

public sealed class SettlementDeliveryTracker
{
    private readonly ConcurrentDictionary<ulong, string> latest = new();

    public SettlementDeliveryTracker(LobbyRoles roles)
    {
        roles.RoleOffline += uid =>
        {
            latest.TryRemove(uid, out _);
            return Task.CompletedTask;
        };
    }

    public void Begin(ulong uid, Guid settlementID) => latest[uid] = settlementID.ToString("N");

    public bool IsCurrent(SettlementKey key) => latest.TryGetValue(key.GameUlongUID, out var current) && current == key.SettlementID;

    public bool AllowsReplay(SettlementKey key) => !latest.TryGetValue(key.GameUlongUID, out var current) || current == key.SettlementID;
}
