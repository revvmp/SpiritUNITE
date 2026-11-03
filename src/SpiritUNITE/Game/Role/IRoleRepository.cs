using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Role;

public interface IRoleRepository
{
    Task<LMNRoleInfo?> FindAsync(ulong gameUlongUID);

    Task SaveAsync(LMNRoleInfo role);

    Task<PB_ENUM_RES_ERROR_CODE> RegisterAsync(ulong gameUlongUID, string userName);
}
