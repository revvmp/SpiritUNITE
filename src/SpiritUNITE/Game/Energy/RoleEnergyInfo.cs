namespace SpiritUNITE.Game.Energy;

public sealed record RoleEnergyInfo(int Energy, int EnergyFreeCapacity, long NextFlushTime)
{
    public RoleEnergyInfo FlushedAt(DateTimeOffset now) => now.ToUnixTimeSeconds() < NextFlushTime
        ? this
        : this with { EnergyFreeCapacity = EnergyCenter.WeeklyEnergyFreeCapacity, NextFlushTime = EnergyCenter.FindNextFlushTime(now) };

    public int Receivable(int energyGain) => Math.Clamp(energyGain, 0, Math.Min(EnergyFreeCapacity, int.MaxValue - Energy));

    public RoleEnergyInfo Gain(int energyGain)
    {
        var received = Receivable(energyGain);
        return this with { Energy = Energy + received, EnergyFreeCapacity = EnergyFreeCapacity - received };
    }
}
