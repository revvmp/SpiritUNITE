using System.Collections.Concurrent;

namespace SpiritUNITE.Game.Role;

public sealed class RoleInfoStore(IRoleRepository repository)
{
    private const ulong DisplayNameRange = 100000;

    private readonly ConcurrentDictionary<ulong, RoleInfo> roles = new();

    public async Task<RoleInfo> GetOrCreateAsync(ulong uid)
    {
        if (roles.TryGetValue(uid, out var cached))
        {
            return cached;
        }

        var role = await repository.FindAsync(uid);
        if (role is null)
        {
            role = new RoleInfo(uid, $"Trainer{uid % DisplayNameRange}");
            await repository.SaveAsync(role);
        }

        return roles.GetOrAdd(uid, role);
    }

    public RoleInfo? Find(ulong uid) => roles.GetValueOrDefault(uid);

    public Task SaveAsync(RoleInfo role) => repository.SaveAsync(role);
}
