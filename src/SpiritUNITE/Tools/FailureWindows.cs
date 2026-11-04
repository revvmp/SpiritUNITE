namespace SpiritUNITE.Tools;

public sealed class FailureWindows<TKey>(TimeProvider clock, TimeSpan window, int capacity)
    where TKey : notnull
{
    private readonly Lock gate = new();
    private readonly Dictionary<TKey, LinkedListNode<FailureWindow>> windows = [];
    private readonly LinkedList<FailureWindow> oldestFirst = [];

    public int Failures(TKey key)
    {
        lock (gate)
        {
            return windows.TryGetValue(key, out var node) && node.Value.EndsAt > clock.GetUtcNow() ? node.Value.Failures : 0;
        }
    }

    public bool TryAdd(TKey key, int limit)
    {
        lock (gate)
        {
            var current = OpenWindow(key, clock.GetUtcNow()).Value;
            if (current.Failures >= limit)
            {
                return false;
            }

            current.Failures++;
            return true;
        }
    }

    public void Add(TKey key) => TryAdd(key, int.MaxValue);

    public void Release(TKey key)
    {
        lock (gate)
        {
            if (windows.TryGetValue(key, out var node) && node.Value.Failures > 0)
            {
                node.Value.Failures--;
            }
        }
    }

    public void Remove(TKey key)
    {
        lock (gate)
        {
            if (windows.Remove(key, out var node))
            {
                oldestFirst.Remove(node);
            }
        }
    }

    private LinkedListNode<FailureWindow> OpenWindow(TKey key, DateTimeOffset now)
    {
        if (windows.TryGetValue(key, out var node))
        {
            if (node.Value.EndsAt <= now)
            {
                Restart(node, now);
            }

            return node;
        }

        MakeRoom(now);
        node = oldestFirst.AddLast(new FailureWindow(key, now + window));
        windows.Add(key, node);
        return node;
    }

    private void Restart(LinkedListNode<FailureWindow> node, DateTimeOffset now)
    {
        node.Value.Failures = 0;
        node.Value.EndsAt = now + window;
        oldestFirst.Remove(node);
        oldestFirst.AddLast(node);
    }

    private void MakeRoom(DateTimeOffset now)
    {
        while (oldestFirst.First is { } oldest && (oldest.Value.EndsAt <= now || windows.Count >= capacity))
        {
            oldestFirst.RemoveFirst();
            windows.Remove(oldest.Value.Key);
        }
    }

    private sealed class FailureWindow(TKey key, DateTimeOffset endsAt)
    {
        public TKey Key { get; } = key;

        public DateTimeOffset EndsAt { get; set; } = endsAt;

        public int Failures { get; set; }
    }
}
