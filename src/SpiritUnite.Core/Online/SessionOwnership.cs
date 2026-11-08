using SpiritUnite.Network.Gcp;

namespace SpiritUnite.Online;

internal sealed class SessionOwnership
{
    private readonly Lock gate = new();
    private readonly Dictionary<ulong, GcpSession> owners = [];

    public async Task ClaimAsync(ulong uid, GcpSession session)
    {
        GcpSession? previous;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(!session.IsOpen, session);
            previous = owners.GetValueOrDefault(uid);
            owners[uid] = session;
        }

        if (previous is not null && previous != session)
        {
            await previous.DisposeAsync();
        }
    }

    public void Use(ulong uid, GcpSession session, Action action)
    {
        lock (gate)
        {
            if (!session.IsOpen || owners.GetValueOrDefault(uid) != session)
            {
                throw new InvalidOperationException("The session has been superseded");
            }

            action();
        }
    }

    public bool IsCurrent(ulong uid, GcpSession session)
    {
        lock (gate)
        {
            return owners.GetValueOrDefault(uid) == session;
        }
    }

    public bool Release(ulong uid, GcpSession session)
    {
        lock (gate)
        {
            if (owners.GetValueOrDefault(uid) == session)
            {
                owners.Remove(uid);
                return true;
            }

            return false;
        }
    }
}
