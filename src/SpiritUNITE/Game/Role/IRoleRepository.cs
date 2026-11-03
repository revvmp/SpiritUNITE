namespace SpiritUNITE.Game.Role;

public interface IRoleRepository
{
    Task<RoleInfo?> FindAsync(ulong gameUlongUID);

    Task SaveAsync(RoleInfo role);
}
