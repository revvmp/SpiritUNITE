using SpiritUNITE.Databin;
using SpiritUNITE.Tools;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.BattlePass;

public readonly record struct BattlePassSeasonState(pbBattlePassSeason Season, PB_ENUM_ENM_BATTLEPASS_SEASON_STATE SeasonState)
{
    public bool IsInProcess => SeasonState == PB_ENUM_ENM_BATTLEPASS_SEASON_STATE.PbEnmBattlepassSeasonStateProcess;

    public long StartTime => UTCTimeUtil.CfgTime2ServerUnixTime(Season.SeasonStartTime);

    public long EndTime => UTCTimeUtil.CfgTime2ServerUnixTime(Season.SeasonEndTime);
}

public sealed class BattlePassSeasons(GameData gameData)
{
    public const int StartingLevel = 1;
    public const int FreePassType = (int)PB_ENUM_ENM_BATTLEPASS_PASSTYPE.PbEnmBattlepassPasstypeInvalid;

    private const int FirstWeekIndex = 1;
    private const int DaysPerWeek = 7;
    private const int NoTask = 0;

    private readonly ILookup<int, pbBattlePassLevel> levelsBySeason = gameData.BattlePassLevel.Rows.ToLookup(row => row.SeasonID);

    private readonly ILookup<int, pbBattlePassLevelReward> rewardsBySeason = gameData.BattlePassLevelReward.Rows.ToLookup(row => row.SeasonID);

    private readonly ILookup<int, pbDrop_Static> dropsById = gameData.DropStatic.Rows.OrderBy(drop => drop.SDropSeq).ToLookup(drop => drop.SDropID);

    public GameData GameData => gameData;

    public BattlePassSeasonState FindCurrentSeason(DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        var seasons = gameData.BattlePassSeason.Rows;
        var started = seasons
            .Where(season => UTCTimeUtil.CfgTime2ServerUnixTime(season.SeasonStartTime) <= nowSeconds)
            .MaxBy(season => UTCTimeUtil.CfgTime2ServerUnixTime(season.SeasonStartTime));
        if (started is null)
        {
            var first = seasons.MinBy(season => UTCTimeUtil.CfgTime2ServerUnixTime(season.SeasonStartTime))!;
            return new(first, PB_ENUM_ENM_BATTLEPASS_SEASON_STATE.PbEnmBattlepassSeasonStateNull);
        }

        var state = nowSeconds <= UTCTimeUtil.CfgTime2ServerUnixTime(started.SeasonEndTime)
            ? PB_ENUM_ENM_BATTLEPASS_SEASON_STATE.PbEnmBattlepassSeasonStateProcess
            : PB_ENUM_ENM_BATTLEPASS_SEASON_STATE.PbEnmBattlepassSeasonStateOver;
        return new(started, state);
    }

    public pbBattlePassSeason? FindSeason(int seasonID) => gameData.BattlePassSeason.Find(seasonID);

    public bool IsOver(int seasonID, DateTimeOffset now) =>
        FindSeason(seasonID) is not { } season || UTCTimeUtil.CfgTime2ServerUnixTime(season.SeasonEndTime) < now.ToUnixTimeSeconds();

    public int FindWeekIndex(BattlePassSeasonState current, DateTimeOffset now)
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(now.ToUnixTimeSeconds(), current.StartTime, current.EndTime));
        var weeksSinceStart = (FindWeekStart(at) - FindWeekStart(DateTimeOffset.FromUnixTimeSeconds(current.StartTime))).Days / DaysPerWeek;
        return FirstWeekIndex + weeksSinceStart;
    }

    private static DateTimeOffset FindWeekStart(DateTimeOffset time)
    {
        var day = new DateTimeOffset(time.UtcDateTime.Date, TimeSpan.Zero);
        return day.AddDays(-(((int)day.DayOfWeek + DaysPerWeek - (int)DayOfWeek.Monday) % DaysPerWeek));
    }

    public int FindMaxLevel(int seasonID) =>
        levelsBySeason[seasonID].Select(row => row.Level).DefaultIfEmpty(StartingLevel).Max();

    public pbBattlePassLevel? FindLevel(int seasonID, int level) =>
        gameData.BattlePassLevel.Find(pbBattlePassLevel.Key(seasonID, level));

    public pbBattlePassLevelReward? FindLevelReward(int seasonID, int level) =>
        gameData.BattlePassLevelReward.Find(pbBattlePassLevelReward.Key(seasonID, level));

    public IEnumerable<pbBattlePassLevelReward> LevelRewardsUpTo(int seasonID, int level) =>
        rewardsBySeason[seasonID].Where(row => row.Level <= level).OrderBy(row => row.Level);

    public pbBattlePassInfo? FindPassInfo(int seasonID) => gameData.BattlePassInfo.Find(seasonID);

    public IEnumerable<BattlePassResource> FindDropResources(int dropID) =>
        dropsById[dropID].Select(drop => new BattlePassResource(drop.ResID, drop.ResNum));

    public IEnumerable<BattlePassTaskSlot> FindTaskSlots(BattlePassSeasonState current, DateTimeOffset now)
    {
        var seasonID = current.Season.SeasonID;
        var weekIndex = FindWeekIndex(current, now);
        var seasonTasks = gameData.BattlePassSeasonTask.Rows.Where(row => row.SeasonID == seasonID && row.WeekIndex <= weekIndex);
        foreach (var row in seasonTasks)
        {
            foreach (var taskID in row.TaskID.Where(IsTask))
            {
                yield return new(taskID, PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemBattlepassSeasontask, false);
            }

            foreach (var taskID in row.WelfareTaskID.Where(IsTask))
            {
                yield return new(taskID, PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemBattlepassSeasontask, true);
            }
        }

        var weekGroups = gameData.BattlePassWeekIndex.Rows
            .Where(row => row.SeasonID == seasonID && row.WeekIndex <= weekIndex)
            .Select(row => gameData.BattlePassWeekTaskGroup.Find(row.TaskGroupID))
            .OfType<pbBattlePass_WeekTaskGroup>();
        foreach (var group in weekGroups)
        {
            foreach (var taskID in group.BPWeekTask.Select(task => task.TaskID).Where(IsTask))
            {
                yield return new(taskID, PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemBattlepassWeektask, false);
            }

            foreach (var taskID in group.WelfareTask.Select(task => task.TaskID).Where(IsTask))
            {
                yield return new(taskID, PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemBattlepassWeektask, true);
            }
        }

        foreach (var row in FindTimeLimitTasks(seasonID, now))
        {
            yield return new(row.TimeLimitTaskID, PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemBattlepassTimelimittask, false);
        }
    }

    public IEnumerable<pbBattlePass_TimeLimitTask> FindTimeLimitTasks(int seasonID, DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        return gameData.BattlePassTimeLimitTask.Rows.Where(row =>
            row.SeasonID == seasonID
            && IsTask(row.TimeLimitTaskID)
            && UTCTimeUtil.CfgTime2ServerUnixTime(row.BeginTime) <= nowSeconds
            && nowSeconds <= UTCTimeUtil.CfgTime2ServerUnixTime(row.EndTime));
    }

    private static bool IsTask(int taskID) => taskID != NoTask;
}

public sealed record BattlePassTaskSlot(int TaskID, PB_ENUM_ENM_TASKSYSTEM TaskSystem, bool IsWelfare);
