namespace SpiritUnite.Game.Energy;

public sealed record EnergyRules(int WeeklyEnergyFreeCapacity, int EnergyTicketCostEnergy, int NewPlayerDefaultEnergy, int DailyFreeDrawCount);

public sealed record EnergyAccelerator(int EnergyAccelerateRatio, int EnergyAccelerateCapacity);

public sealed record RoleEnergyInfo(
    int Energy,
    int RaffleTicket,
    int EnergyFreeCapacity,
    long NextFlushTime,
    int TodayDrawNum,
    int DailyFreeDrawUseNum,
    long NextDrawResetTime)
{
    public const int NoEnergyAccelerateRatio = 0;

    public int EnergyPayCapacity { get; init; }

    public int EnergyAccelerateRatio { get; init; }

    public IReadOnlyList<EnergyAccelerator> Accelerators { get; init; } = [];

    public int CollectableCapacity => EnergyFreeCapacity + EnergyPayCapacity;

    public int AcceleratorCapacityTotal => Accelerators.Sum(accelerator => accelerator.EnergyAccelerateCapacity);

    public int InstalledAcceleratorCapacity =>
        Accelerators.FirstOrDefault(accelerator => accelerator.EnergyAccelerateRatio == EnergyAccelerateRatio)?.EnergyAccelerateCapacity ?? 0;

    public RoleEnergyInfo FlushedAt(DateTimeOffset now, int weeklyEnergyFreeCapacity)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        var flushed = nowSeconds < NextFlushTime
            ? this
            : this with { EnergyFreeCapacity = weeklyEnergyFreeCapacity, NextFlushTime = EnergyCenter.FindNextFlushTime(now) };
        return nowSeconds < NextDrawResetTime
            ? flushed
            : flushed with { TodayDrawNum = 0, DailyFreeDrawUseNum = 0, NextDrawResetTime = EnergyCenter.FindNextDrawResetTime(now) };
    }

    public int Receivable(int energyGain) => Math.Clamp(energyGain, 0, EnergyFreeCapacity);

    public RoleEnergyInfo Gain(int energyGain, int energyTicketCostEnergy)
    {
        var received = Receivable(energyGain);
        return Charge(received, energyTicketCostEnergy) with { EnergyFreeCapacity = EnergyFreeCapacity - received };
    }

    public RoleEnergyInfo GainFromBattle(int energyGain, int energyTicketCostEnergy)
    {
        var earned = Math.Max(0, energyGain);
        var fromFree = Math.Min(earned, EnergyFreeCapacity);
        var fromPay = Math.Min(earned - fromFree, EnergyPayCapacity);
        var accelerated = Math.Min(AcceleratedShareOf(fromFree + fromPay), InstalledAcceleratorCapacity);
        var drained = DrainInstalledAccelerator(accelerated);
        return drained.Charge(fromFree + fromPay + accelerated, energyTicketCostEnergy) with
        {
            EnergyFreeCapacity = EnergyFreeCapacity - fromFree,
            EnergyPayCapacity = EnergyPayCapacity - fromPay,
        };
    }

    public RoleEnergyInfo AddPayCapacity(int energyPayCapacity) => this with { EnergyPayCapacity = EnergyPayCapacity + energyPayCapacity };

    public RoleEnergyInfo Install(int energyAccelerateRatio) => this with { EnergyAccelerateRatio = energyAccelerateRatio };

    public int UnusedDailyFreeDraws(int dailyFreeDrawCount) => Math.Max(0, dailyFreeDrawCount - DailyFreeDrawUseNum);

    public int CountDrawRedDot(int dailyFreeDrawCount) => RaffleTicket + UnusedDailyFreeDraws(dailyFreeDrawCount);

    private int AcceleratedShareOf(int received) =>
        EnergyAccelerateRatio > 1 ? (int)Math.Min(int.MaxValue, (long)received * (EnergyAccelerateRatio - 1)) : 0;

    private RoleEnergyInfo DrainInstalledAccelerator(int accelerated)
    {
        if (accelerated <= 0)
        {
            return this;
        }

        var remaining = InstalledAcceleratorCapacity - accelerated;
        return this with
        {
            Accelerators =
            [
                .. Accelerators
                    .Select(accelerator => accelerator.EnergyAccelerateRatio == EnergyAccelerateRatio
                        ? accelerator with { EnergyAccelerateCapacity = remaining }
                        : accelerator)
                    .Where(accelerator => accelerator.EnergyAccelerateCapacity > 0),
            ],
            EnergyAccelerateRatio = remaining > 0 ? EnergyAccelerateRatio : NoEnergyAccelerateRatio,
        };
    }

    private RoleEnergyInfo Charge(int received, int energyTicketCostEnergy)
    {
        var charged = Energy + received;
        return this with
        {
            Energy = charged % energyTicketCostEnergy,
            RaffleTicket = RaffleTicket + charged / energyTicketCostEnergy,
        };
    }
}
