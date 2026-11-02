using System.Collections.Concurrent;

namespace SpiritUNITE.Game.Role;

public sealed class RoleInfoStore
{
    private const ulong DisplayNameRange = 100000;

    private readonly ConcurrentDictionary<ulong, RoleInfo> roles = new();

    public RoleInfo GetOrCreate(ulong uid) =>
        roles.GetOrAdd(uid, id => new RoleInfo(id, $"Trainer{id % DisplayNameRange}"));

    public RoleInfo? Find(ulong uid) => roles.GetValueOrDefault(uid);
}
