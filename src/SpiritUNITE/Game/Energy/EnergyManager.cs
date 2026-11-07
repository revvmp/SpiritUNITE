using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;

namespace SpiritUNITE.Game.Energy;

public sealed class EnergyManager(EnergyRepository repository, GameData gameData, TimeProvider clock)
{
    public async Task<RoleEnergyInfo> LoadAsync(ulong gameUlongUID)
    {
        var now = clock.GetUtcNow();
        var weeklyEnergyFreeCapacity = EnergyCenter.WeeklyEnergyFreeCapacity(gameData);
        return (await repository.FindAsync(gameUlongUID))?.FlushedAt(now, weeklyEnergyFreeCapacity)
            ?? EnergyCenter.Starting(now, weeklyEnergyFreeCapacity);
    }
}
