using Microsoft.Extensions.Logging;

namespace SpiritUNITE.Tools;

public static class UidEvents
{
    public static async Task RaiseEachAsync(Func<ulong, Task>? handlers, IEnumerable<ulong> uids, ILogger logger, string eventName)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var uid in uids)
        {
            foreach (var handler in handlers.GetInvocationList().Cast<Func<ulong, Task>>())
            {
                try
                {
                    await handler(uid);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "{EventName} handler failed for uid {Uid}", eventName, uid);
                }
            }
        }
    }
}
