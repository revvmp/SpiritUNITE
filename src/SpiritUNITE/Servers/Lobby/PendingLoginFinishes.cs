using System.Collections.Concurrent;

namespace SpiritUNITE.Servers.Lobby;

public sealed class PendingLoginFinishes
{
    private readonly ConcurrentDictionary<ulong, bool> offingRestartByUid = new();

    public void Await(ulong uid, bool offingRestart) => offingRestartByUid[uid] = offingRestart;

    public bool TryTake(ulong uid, out bool offingRestart) => offingRestartByUid.TryRemove(uid, out offingRestart);
}
