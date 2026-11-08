using SpiritUNITE.Databin;
using SpiritUNITE.Tools;

namespace SpiritUNITE.Game.Room;

public readonly record struct SceneMapStatus(bool IsOpen, bool HasNextOpen);

public static class SceneMapOpening
{
    public const int LockedMap = 3;
    public const int LimitedTimeMapNotOpen = 4;

    private const int NormalOpenMode = 0;
    private const int CycleOpenMode = 1;
    private const int SecondsPerHour = 3600;
    private const int MaxCyclesSearched = 100;
    private const int TowerDefenseGameType = 8;

    public static int CanUseTypeOf(GameData gameData, int sceneId, DateTimeOffset now) =>
        IsSceneMapOpening(gameData, sceneId, now) switch
        {
            null or { IsOpen: true } => RoomOptions.CanUseMap,
            { HasNextOpen: true } => LimitedTimeMapNotOpen,
            _ => LockedMap,
        };

    public static bool IsHiddenEventMap(GameData gameData, int sceneId, DateTimeOffset now) =>
        gameData.SceneMap.Find(sceneId) is { GameType: TowerDefenseGameType } && !IsTowerDefensePeriodShown(gameData, now);

    public static SceneMapStatus? IsSceneMapOpening(GameData gameData, int sceneId, DateTimeOffset now)
    {
        var rows = gameData.SceneOpenTime.Rows.Where(row => row.SceneId == sceneId).ToList();
        if (rows.Count == 0)
        {
            return null;
        }

        if (rows[0].IsShowDate > 0)
        {
            return new SceneMapStatus(true, false);
        }

        var serverNow = now.ToUnixTimeSeconds();
        var isOpen = false;
        var hasNextOpen = false;
        foreach (var row in rows)
        {
            switch (row.OpenMode)
            {
                case NormalOpenMode:
                    var status = NormalMapStatus(row, serverNow);
                    isOpen |= status.IsOpen;
                    hasNextOpen |= status.HasNextOpen;
                    break;
                case CycleOpenMode:
                    return CycleMapStatus(gameData, row, serverNow);
            }
        }

        return new SceneMapStatus(isOpen, !isOpen && hasNextOpen);
    }

    private static SceneMapStatus NormalMapStatus(pbScene_OpenTime row, long serverNow) =>
        StatusInRange(serverNow, UTCTimeUtil.CfgTime2ServerUnixTime(row.OpenDate), UTCTimeUtil.CfgTime2ServerUnixTime(row.CloseDate));

    private static SceneMapStatus CycleMapStatus(GameData gameData, pbScene_OpenTime row, long serverNow)
    {
        var circleMapNum = gameData.SceneOpenTime.Rows.Count(other =>
            other.OpenMode == CycleOpenMode && other.OpenModeParaA == row.OpenModeParaA);
        var openIntervalSecond = (long)row.OpenModeParaC * SecondsPerHour;
        var oneCycleSecond = circleMapNum * openIntervalSecond;
        if (oneCycleSecond <= 0)
        {
            return new SceneMapStatus(false, false);
        }

        var openDate = UTCTimeUtil.CfgTime2ServerUnixTime(row.OpenDate);
        var closeDate = UTCTimeUtil.CfgTime2ServerUnixTime(row.CloseDate);
        var currCycleCount = (long)Math.Floor((double)(serverNow - openDate) / oneCycleSecond);
        var firstCycle = Math.Max(currCycleCount, 0);
        for (var cycle = firstCycle; cycle <= firstCycle + MaxCyclesSearched; cycle++)
        {
            var start = openDate + (cycle * oneCycleSecond) + ((row.OpenModeParaB - 1) * openIntervalSecond);
            var rangeStart = Math.Max(start, openDate);
            var rangeEnd = Math.Min(start + openIntervalSecond, closeDate);
            if (rangeStart >= rangeEnd)
            {
                return new SceneMapStatus(false, false);
            }

            var status = StatusInRange(serverNow, rangeStart, rangeEnd);
            if (status.IsOpen)
            {
                return cycle == currCycleCount ? status : new SceneMapStatus(false, true);
            }

            if (status.HasNextOpen)
            {
                return status;
            }
        }

        return new SceneMapStatus(false, false);
    }

    private static SceneMapStatus StatusInRange(long serverNow, long openStamp, long closeStamp) =>
        serverNow < openStamp ? new SceneMapStatus(false, true)
        : serverNow <= closeStamp ? new SceneMapStatus(true, false)
        : new SceneMapStatus(false, false);

    private static bool IsTowerDefensePeriodShown(GameData gameData, DateTimeOffset now)
    {
        var cfgNow = UTCTimeUtil.ServerTime2CfgTime(now);
        return gameData.TowerDefenseBase.Rows.Any(period => period.StartTime <= cfgNow && cfgNow <= period.DisplayEndTime);
    }
}
