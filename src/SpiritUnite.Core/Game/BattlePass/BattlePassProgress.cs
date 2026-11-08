using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.BattlePass;

public sealed record BattlePassLevelRewardKey(int Level, PB_ENUM_ENM_BATTLEPASS_LEVEL_REWARD_TYPE Type);

public sealed record BattlePassProgress(
    int SeasonID,
    int Level,
    int Exp,
    int TotalExp,
    int PassType,
    int VideoPlayed,
    int AdditionTreasureCnt,
    int AdditionTreasureUnGetCnt,
    int AdditionTreasureUnGetExp,
    long LastUpLevelTime,
    long LastGainExpTime,
    bool SeasonSettled)
{
    public const int VideoWatched = 1;

    public static BattlePassProgress Starting(int seasonID) => new(
        seasonID,
        BattlePassSeasons.StartingLevel,
        0,
        0,
        BattlePassSeasons.FreePassType,
        0,
        0,
        0,
        0,
        0,
        0,
        false);

    public bool HasPass => PassType != BattlePassSeasons.FreePassType;

    public pbCOMDT_BATTLEPASS_SEASON_BRIEF ToSeasonBrief(BattlePassSeasonState season, BattlePassTaskFinishCnt finishCnt, int maxRankedDivision) => new()
    {
        SeasonID = SeasonID,
        SeasonStartTime = season.StartTime,
        SeasonEndTime = season.EndTime,
        PassType = PassType,
        Level = Level,
        TotalExp = TotalExp,
        DailyTaskFinishCnt = finishCnt.DailyTaskFinishCnt,
        WeeklyTaskFinishCnt = finishCnt.WeeklyTaskFinishCnt,
        ChallengeTaskFinishCnt = finishCnt.ChallengeTaskFinishCnt,
        AdditionTreasureCnt = AdditionTreasureCnt,
        MaxRankedDivision = maxRankedDivision,
    };
}

public sealed class BattlePassLeveling(BattlePassSeasons seasons)
{
    public BattlePassProgress GainExp(BattlePassProgress progress, int exp, long now)
    {
        if (exp <= 0)
        {
            return progress;
        }

        var gained = progress with
        {
            Exp = AddClamped(progress.Exp, exp),
            TotalExp = AddClamped(progress.TotalExp, exp),
            LastGainExpTime = now,
        };
        return LevelUp(gained, now);
    }

    public BattlePassProgress SetLevel(BattlePassProgress progress, int level, long now) =>
        LevelUp(progress with { Level = level, LastUpLevelTime = now }, now);

    public int FindMaxLevel(BattlePassProgress progress) => seasons.FindMaxLevel(progress.SeasonID);

    public long FindLevelCost(BattlePassProgress progress, int targetLevel) =>
        Enumerable.Range(progress.Level + 1, Math.Max(0, targetLevel - progress.Level))
            .Select(level => (long)(seasons.FindLevel(progress.SeasonID, level)?.UpMoneyCost ?? 0))
            .Sum();

    public int? FindLevelCoinType(BattlePassProgress progress) =>
        seasons.FindLevel(progress.SeasonID, progress.Level)?.UpPassCoinType;

    private BattlePassProgress LevelUp(BattlePassProgress progress, long now)
    {
        var maxLevel = seasons.FindMaxLevel(progress.SeasonID);
        var level = progress.Level;
        var exp = progress.Exp;
        var lastUpLevelTime = progress.LastUpLevelTime;
        while (level < maxLevel && seasons.FindLevel(progress.SeasonID, level) is { UpExp: > 0 } row && exp >= row.UpExp)
        {
            exp -= row.UpExp;
            level++;
            lastUpLevelTime = now;
        }

        var leveled = progress with { Level = level, Exp = exp, LastUpLevelTime = lastUpLevelTime };
        return level >= maxLevel ? FillTreasure(leveled) : leveled;
    }

    private BattlePassProgress FillTreasure(BattlePassProgress progress)
    {
        var season = seasons.FindSeason(progress.SeasonID);
        var boxExp = season?.FullLevelTreasureBoxEXP ?? 0;
        var boxUpper = season?.FullLevelTreasureBoxUpper ?? 0;
        if (boxExp <= 0 || season!.FullLevelTreasureBox == 0)
        {
            return progress with { Exp = 0 };
        }

        var ungetCnt = progress.AdditionTreasureUnGetCnt;
        var ungetExp = AddClamped(progress.AdditionTreasureUnGetExp, progress.Exp);
        while (ungetExp >= boxExp && ungetCnt < boxUpper)
        {
            ungetExp -= boxExp;
            ungetCnt++;
        }

        if (ungetCnt >= boxUpper)
        {
            ungetExp = 0;
        }

        return progress with { Exp = 0, AdditionTreasureUnGetCnt = ungetCnt, AdditionTreasureUnGetExp = ungetExp };
    }

    private static int AddClamped(int first, int second) => (int)Math.Min((long)first + second, int.MaxValue);
}
