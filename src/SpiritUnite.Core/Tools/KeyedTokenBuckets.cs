namespace SpiritUnite.Tools;

public sealed class KeyedTokenBuckets<TKey>(int capacity, long refillIntervalMs, int maxKeys) where TKey : notnull
{
    private const int FullBucketEvictionScan = 8;

    private readonly Lock gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Bucket>> buckets = [];
    private readonly LinkedList<Bucket> leastRecentlyUsedFirst = new();

    public int TrackedKeys
    {
        get
        {
            lock (gate)
            {
                return buckets.Count;
            }
        }
    }

    public bool TryTake(TKey key, long nowMs)
    {
        lock (gate)
        {
            if (buckets.TryGetValue(key, out var node))
            {
                leastRecentlyUsedFirst.Remove(node);
                leastRecentlyUsedFirst.AddLast(node);
            }
            else
            {
                if (buckets.Count >= maxKeys)
                {
                    EvictOne(nowMs);
                }

                node = leastRecentlyUsedFirst.AddLast(new Bucket(key, capacity, nowMs));
                buckets[key] = node;
            }

            var bucket = node.Value;
            Refill(bucket, nowMs);
            if (bucket.Tokens == 0)
            {
                return false;
            }

            bucket.Tokens--;
            return true;
        }
    }

    public void ForgetFull(long nowMs)
    {
        lock (gate)
        {
            var node = leastRecentlyUsedFirst.First;
            while (node is not null)
            {
                var next = node.Next;
                Refill(node.Value, nowMs);
                if (node.Value.Tokens == capacity)
                {
                    Forget(node);
                }

                node = next;
            }
        }
    }

    private void EvictOne(long nowMs)
    {
        var node = leastRecentlyUsedFirst.First;
        for (var scanned = 0; node is not null && scanned < FullBucketEvictionScan; scanned++, node = node.Next)
        {
            Refill(node.Value, nowMs);
            if (node.Value.Tokens == capacity)
            {
                Forget(node);
                return;
            }
        }

        if (leastRecentlyUsedFirst.First is { } oldest)
        {
            Forget(oldest);
        }
    }

    private void Forget(LinkedListNode<Bucket> node)
    {
        buckets.Remove(node.Value.Key);
        leastRecentlyUsedFirst.Remove(node);
    }

    private void Refill(Bucket bucket, long nowMs)
    {
        if (bucket.Tokens == capacity)
        {
            bucket.RefilledAtMs = nowMs;
            return;
        }

        var earned = (nowMs - bucket.RefilledAtMs) / refillIntervalMs;
        if (earned <= 0)
        {
            return;
        }

        bucket.Tokens = (int)Math.Min(capacity, bucket.Tokens + earned);
        bucket.RefilledAtMs = bucket.Tokens == capacity ? nowMs : bucket.RefilledAtMs + earned * refillIntervalMs;
    }

    private sealed class Bucket(TKey key, int tokens, long refilledAtMs)
    {
        public TKey Key { get; } = key;
        public int Tokens { get; set; } = tokens;
        public long RefilledAtMs { get; set; } = refilledAtMs;
    }
}
