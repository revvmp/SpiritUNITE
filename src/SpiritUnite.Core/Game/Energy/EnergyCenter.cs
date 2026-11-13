using SpiritUnite.Databin;
using SpiritUnite.Tools;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Energy;

public sealed record EnergyPrize(int RewardId, int RewardProbability, int ResourceId, int ResourceNum);

public static class EnergyCenter
{
    private const int DaysPerWeek = 7;
    private const int NoFirstLottery = 0;
    private const int AcceleratorRatioParamIndex = 0;
    private const int AcceleratorCapacityParamIndex = 1;
    private const int PayCapacityParamIndex = 0;

    public static int WeeklyEnergyFreeCapacity(GameData gameData) => gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalEnergyPoolLimit);

    public static EnergyRules Rules(GameData gameData) => new(
        WeeklyEnergyFreeCapacity(gameData),
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalEnergyTicketCostEnergy),
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalNewPlayerDefaultEnergy),
        gameData.GetGlobalIntData(PB_ENUM_RES_RESGLOBAL.PbResglobalEnergyDailyFreeDrawCount));

    public static RoleEnergyInfo Starting(DateTimeOffset now, EnergyRules rules)
    {
        var empty = new RoleEnergyInfo(0, 0, rules.WeeklyEnergyFreeCapacity, FindNextFlushTime(now), 0, 0, FindNextDrawResetTime(now));
        var charged = empty.Gain(rules.NewPlayerDefaultEnergy, rules.EnergyTicketCostEnergy);
        return charged with { EnergyFreeCapacity = rules.WeeklyEnergyFreeCapacity };
    }

    public static pbCOMDT_ENERGY_INFO BuildEnergyInfo(RoleEnergyInfo energy) => new()
    {
        Energy = energy.Energy,
        RaffleTicket = energy.RaffleTicket,
        TodayDrawNum = energy.TodayDrawNum,
        EnergyFreeCapacity = energy.EnergyFreeCapacity,
        EnergyPayCapacity = energy.EnergyPayCapacity,
        EnergyAccelerateRatio = energy.EnergyAccelerateRatio,
        EnergyAccelerateInfo = { energy.Accelerators.Select(BuildAccelerateInfo) },
    };

    public static pbCOMDT_ENERGY_ACCELERATE_INFO BuildAccelerateInfo(EnergyAccelerator accelerator) => new()
    {
        EnergyAccelerateRatio = accelerator.EnergyAccelerateRatio,
        EnergyAccelerateCapacity = accelerator.EnergyAccelerateCapacity,
    };

    public static long FindNextDrawResetTime(DateTimeOffset now) =>
        new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1).ToUnixTimeSeconds();

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
        DailyFreeDrawUseNum = energy.DailyFreeDrawUseNum,
    };

    public static pbSCGetEnergyInfoRsp BuildGetEnergyInfoRsp(RoleEnergyInfo energy) => new()
    {
        ResultInfo = new pbResultInfo(),
        EnergyInfo = BuildEnergyInfo(energy),
        NextFlushTime = energy.NextFlushTime,
        DailyFreeDrawUseNum = energy.DailyFreeDrawUseNum,
    };

    public static EnergyPrize[] FindPrizes(GameData gameData, PB_ENUM_ENUM_ENERGY_POOL_TYPE poolType, int lotteryPoolId) =>
        poolType == PB_ENUM_ENUM_ENERGY_POOL_TYPE.PbEnumEnergyPoolTypeBadge
            ? [.. gameData.BadgeReward.Rows
                .Where(row => row.LotteryPoolId == lotteryPoolId && row.FirstLottery == NoFirstLottery && row.RewardProbability > 0)
                .OrderBy(row => row.RewardId)
                .Select(row => new EnergyPrize(row.RewardId, row.RewardProbability, row.ResourceId, row.ResourceNum))]
            : [.. gameData.LotteryReward.Rows
                .Where(row => row.LotteryPoolId == lotteryPoolId && row.FirstLottery == NoFirstLottery && row.RewardProbability > 0)
                .OrderBy(row => row.RewardId)
                .Select(row => new EnergyPrize(row.RewardId, row.RewardProbability, row.ResourceId, row.ResourceNum))];

    public static EnergyAccelerator? FindAccelerator(pbOutSideItem_Base config)
    {
        var ratio = config.ItemParam.ElementAtOrDefault(AcceleratorRatioParamIndex);
        var capacity = config.ItemParam.ElementAtOrDefault(AcceleratorCapacityParamIndex);
        return config.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeEnergyaccelerate
            && IsAccelerateRatio(ratio)
            && capacity > 0
                ? new EnergyAccelerator(ratio, capacity)
                : null;
    }

    public static int? FindPayCapacity(pbOutSideItem_Base config)
    {
        var capacity = config.ItemParam.ElementAtOrDefault(PayCapacityParamIndex);
        return config.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeEnergypaycapacity && capacity > 0
            ? capacity
            : null;
    }

    public static bool IsAccelerateRatio(int ratio) =>
        ratio != RoleEnergyInfo.NoEnergyAccelerateRatio && Enum.IsDefined((PB_ENUM_RES_ENERGY_ACCELERATE_RATIO)ratio);

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
