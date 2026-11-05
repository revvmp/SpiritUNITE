using SpiritUNITE.Databin;
using SpiritUNITE.Tools;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.BattlePass;

public readonly record struct BattlePassSeasonState(pbBattlePassSeason Season, PB_ENUM_ENM_BATTLEPASS_SEASON_STATE SeasonState)
{
    public bool IsInProcess => SeasonState == PB_ENUM_ENM_BATTLEPASS_SEASON_STATE.PbEnmBattlepassSeasonStateProcess;
}

public static class BattlePassSeasons
{
    public const int StartingLevel = 1;
    public const int FreePassType = 0;

    private const int FirstWeekIndex = 1;
    private const int DaysPerWeek = 7;

    public static BattlePassSeasonState FindCurrentSeason(GameData gameData, DateTimeOffset now)
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

    public static pbCOMDT_BATTLEPASS_INFO BuildStartingInfo(BattlePassSeasonState current, DateTimeOffset now)
    {
        if (!current.IsInProcess)
        {
            return BuildNoSeasonInfo();
        }

        var startTime = UTCTimeUtil.CfgTime2ServerUnixTime(current.Season.SeasonStartTime);
        var endTime = UTCTimeUtil.CfgTime2ServerUnixTime(current.Season.SeasonEndTime);
        return new pbCOMDT_BATTLEPASS_INFO
        {
            SeasonInfo = new pbCOMDT_BATTLEPASS_SEASON_INFO
            {
                SeasonID = current.Season.SeasonID,
                SeasonStartTime = startTime,
                SeasonEndTime = endTime,
                SeasonState = (int)current.SeasonState,
                Level = StartingLevel,
                PassType = FreePassType,
                LevelReward = new pbCOMDT_LEVEL_REWARD_INFO(),
            },
            WeekIndex = FindWeekIndex(startTime, endTime, now.ToUnixTimeSeconds()),
            SeasonHistory = new pbCOMDT_BATTLEPASS_HISTORY_INFO(),
        };
    }

    public static pbSCBattlePassNtf BuildBattlePassNtf(GameData gameData, DateTimeOffset now) => new()
    {
        BattlePassInfo = BuildStartingInfo(FindCurrentSeason(gameData, now), now),
    };

    private static pbCOMDT_BATTLEPASS_INFO BuildNoSeasonInfo() => new()
    {
        SeasonInfo = new pbCOMDT_BATTLEPASS_SEASON_INFO { LevelReward = new pbCOMDT_LEVEL_REWARD_INFO() },
        SeasonHistory = new pbCOMDT_BATTLEPASS_HISTORY_INFO(),
    };

    private static int FindWeekIndex(long startTime, long endTime, long now)
    {
        var elapsed = Math.Clamp(now, startTime, endTime) - startTime;
        return FirstWeekIndex + (int)(elapsed / TimeSpan.FromDays(DaysPerWeek).TotalSeconds);
    }
}
