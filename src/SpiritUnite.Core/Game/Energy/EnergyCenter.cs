using SpiritUnite.Databin;
using SpiritUnite.Tools;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Energy;

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

    public static int FindCurrentRewardPoolId(GameData gameData, DateTimeOffset now, PB_ENUM_ENUM_ENERGY_POOL_TYPE poolType) =>
        poolType == PB_ENUM_ENUM_ENERGY_POOL_TYPE.PbEnumEnergyPoolTypeBadge
            ? FindCurrentPoolId(gameData.BadgeRewardPool.Rows.Select(pool => (pool.LotteryPoolId, pool.StartTime, pool.EndTime)), now)
            : FindCurrentPoolId(gameData.RewardPool.Rows.Select(pool => (pool.LotteryPoolId, pool.StartTime, pool.EndTime)), now);

    private static int FindCurrentPoolId(IEnumerable<(int LotteryPoolId, long StartTime, long EndTime)> poolRows, DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        var pools = poolRows
            .Select(pool => (pool.LotteryPoolId, Start: UTCTimeUtil.CfgTime2ServerUnixTime(pool.StartTime), End: UTCTimeUtil.CfgTime2ServerUnixTime(pool.EndTime)))
            .ToArray();
        var started = pools.Where(pool => pool.Start <= nowSeconds).ToArray();
        if (started.Length == 0)
        {
            return pools.MinBy(pool => pool.Start).LotteryPoolId;
        }

        return started.Any(pool => nowSeconds <= pool.End)
            ? started.First(pool => nowSeconds <= pool.End).LotteryPoolId
            : started.MaxBy(pool => pool.Start).LotteryPoolId;
    }
}
