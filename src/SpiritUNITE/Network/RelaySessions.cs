using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Network.Gcp;
using SpiritUNITE.Packets;

namespace SpiritUNITE.Network;

public enum RelayChannel
{
    Stream,
    Unreliable,
}

public sealed class RelaySessions(ILogger<RelaySessions> logger)
{
    private readonly ConcurrentDictionary<ulong, GcpSession> sessions = new();

    public event Func<ulong, Task>? RelayOnline;

    public event Func<ulong, Task>? RelayOffline;

    public string AdvertisedHost { get; private set; } = string.Empty;

    public int AdvertisedPort { get; private set; }

    public bool IsOnline(ulong uid) => sessions.ContainsKey(uid);

    public async Task<bool> PushAsync(ulong uid, RelayChannel channel, params GamePacket[] packets)
    {
        if (packets.Length == 0 || !sessions.TryGetValue(uid, out var session))
        {
            return false;
        }

        try
        {
            await (channel == RelayChannel.Unreliable ? session.PushUnreliableAsync(packets) : session.PushAsync(packets));
            return true;
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            logger.LogInformation("Relay push to uid {Uid} failed: {Reason}", uid, exception.Message);
            return false;
        }
    }

    public void Close(ulong uid)
    {
        if (sessions.TryGetValue(uid, out var session))
        {
            session.Close();
        }
    }

    internal void Advertise(string host, int port)
    {
        AdvertisedHost = host;
        AdvertisedPort = port;
    }

    internal async Task AttachAsync(ulong uid, GcpSession session)
    {
        sessions[uid] = session;
        logger.LogInformation("Uid {Uid} is on the relay", uid);
        await RaiseAsync(RelayOnline, uid);
    }

    internal async Task DetachAsync(ulong uid, GcpSession session)
    {
        if (!sessions.TryRemove(new KeyValuePair<ulong, GcpSession>(uid, session)))
        {
            return;
        }

        logger.LogInformation("Uid {Uid} left the relay", uid);
        await RaiseAsync(RelayOffline, uid);
    }

    private async Task RaiseAsync(Func<ulong, Task>? handlers, ulong uid)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<ulong, Task>>())
        {
            try
            {
                await handler(uid);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Relay presence handler failed for uid {Uid}", uid);
            }
        }
    }
}
