namespace SpiritUNITE.Game.Role;

public interface IRoleRepository
{
    Task<LMNRoleInfo?> FindAsync(ulong gameUlongUID);

    Task SaveAsync(LMNRoleInfo role);
}
