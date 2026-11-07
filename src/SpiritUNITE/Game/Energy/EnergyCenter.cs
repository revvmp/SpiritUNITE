using SpiritUNITE.Databin;
using SpiritUNITE.Tools;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Energy;

public static class EnergyCenter
{
    private const int StartingRaffleTicket = 1;
    private const int DaysPerWeek = 7;

    public static int WeeklyEnergyFreeCapacity(GameData gameData) => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalEnergyPoolLimit);

    public static RoleEnergyInfo Starting(DateTimeOffset now, int weeklyEnergyFreeCapacity) =>
        new(0, weeklyEnergyFreeCapacity, FindNextFlushTime(now));

    public static pbCOMDT_ENERGY_INFO BuildEnergyInfo(RoleEnergyInfo energy) => new()
    {
        Energy = energy.Energy,
        RaffleTicket = StartingRaffleTicket,
        EnergyFreeCapacity = energy.EnergyFreeCapacity,
    };

    public static long FindNextFlushTime(DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + DaysPerWeek) % DaysPerWeek;
        return today.AddDays(daysUntilMonday == 0 ? DaysPerWeek : daysUntilMonday).ToUnixTimeSeconds();
    }

    public static pbSCEnergyInfoNtf BuildEnergyInfoNtf(RoleEnergyInfo energy) => new()
    {
        EnergyInfo = BuildEnergyInfo(energy),
        NextFlushTime = energy.NextFlushTime,
    };

    public static bool IsKnownPoolType(int poolType) => Enum.IsDefined((PB_ENUM_ENUM_ENERGY_POOL_TYPE)poolType);

    public static int FindCurrentRewardPoolId(GameData gameData, DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        var pools = gameData.RewardPool.Rows;
        var started = pools
            .Where(pool => UTCTimeUtil.CfgTime2ServerUnixTime(pool.StartTime) <= nowSeconds)
            .ToArray();
        var current = started.FirstOrDefault(pool => nowSeconds <= UTCTimeUtil.CfgTime2ServerUnixTime(pool.EndTime))
            ?? started.MaxBy(pool => UTCTimeUtil.CfgTime2ServerUnixTime(pool.StartTime))
            ?? pools.MinBy(pool => UTCTimeUtil.CfgTime2ServerUnixTime(pool.StartTime))!;
        return current.LotteryPoolId;
    }
}
