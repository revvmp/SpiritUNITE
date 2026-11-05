using SpiritUNITE.Databin;
using SpiritUNITE.Tools;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Energy;

public static class EnergyCenter
{
    private const int StartingRaffleTicket = 1;
    private const int WeeklyEnergyFreeCapacity = 1400;
    private const int DaysPerWeek = 7;

    public static pbCOMDT_ENERGY_INFO BuildStartingEnergyInfo() => new()
    {
        RaffleTicket = StartingRaffleTicket,
        EnergyFreeCapacity = WeeklyEnergyFreeCapacity,
    };

    public static long FindNextFlushTime(DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + DaysPerWeek) % DaysPerWeek;
        return today.AddDays(daysUntilMonday == 0 ? DaysPerWeek : daysUntilMonday).ToUnixTimeSeconds();
    }

    public static pbSCEnergyInfoNtf BuildEnergyInfoNtf(DateTimeOffset now) => new()
    {
        EnergyInfo = BuildStartingEnergyInfo(),
        NextFlushTime = FindNextFlushTime(now),
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
