using System.Globalization;

namespace SpiritUNITE.Game.Data;

public static class UTCTimeUtil
{
    private const long CfgTimeFieldBase = 100;
    private const string CfgTimeFormat = "yyyyMMddHHmmss";

    public static long ServerTime2CfgTime(DateTimeOffset serverTime) =>
        long.Parse(serverTime.UtcDateTime.ToString(CfgTimeFormat, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    public static long CfgTime2ServerUnixTime(long cfgTime)
    {
        var (rest, second) = Math.DivRem(cfgTime, CfgTimeFieldBase);
        (rest, var minute) = Math.DivRem(rest, CfgTimeFieldBase);
        (rest, var hour) = Math.DivRem(rest, CfgTimeFieldBase);
        (rest, var day) = Math.DivRem(rest, CfgTimeFieldBase);
        var (year, month) = Math.DivRem(rest, CfgTimeFieldBase);

        var monthStart = new DateTimeOffset((int)year, (int)month, 1, 0, 0, 0, TimeSpan.Zero);
        return monthStart
            .AddDays(day - 1)
            .AddHours(hour)
            .AddMinutes(minute)
            .AddSeconds(second)
            .ToUnixTimeSeconds();
    }

    public static bool IsInTime(long timeStart, long timeEnd, long svrConfTime) =>
        timeStart > 0 && timeEnd > 0 && timeStart < svrConfTime && svrConfTime < timeEnd;
}
