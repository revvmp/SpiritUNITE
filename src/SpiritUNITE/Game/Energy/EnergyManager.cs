using SpiritUNITE.Database.Repositories;

namespace SpiritUNITE.Game.Energy;

public sealed class EnergyManager(EnergyRepository repository, TimeProvider clock)
{
    public async Task<RoleEnergyInfo> LoadAsync(ulong gameUlongUID)
    {
        var now = clock.GetUtcNow();
        return (await repository.FindAsync(gameUlongUID))?.FlushedAt(now) ?? EnergyCenter.Starting(now);
    }
}
