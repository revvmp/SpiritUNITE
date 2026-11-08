using Microsoft.Extensions.Logging;
using SpiritUnite.Databin;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Tools;

namespace SpiritUnite.Game.Activity;

public sealed class MatchPredictionManager(
    ActivitySchedule schedule,
    GameData gameData,
    TimeProvider clock,
    ILogger<MatchPredictionManager> logger)
{
    private const int NotAwarded = 0;

    public pbSCMatchPredictionStatusRsp Status() => new()
    {
        ResultInfo = new pbResultInfo(),
        CurrentStatus = (int)CurrentStatus(),
        IsAward = NotAwarded,
    };

    public pbSCMatchPredictionSelectRsp Select(ulong uid, IReadOnlyCollection<pbCOMDT_MATCHPREDICTION_DETAIL> wcsTeamList)
    {
        var result = CurrentStatus() != PB_ENUM_RES_MATCH_PREDICTION_TYPE.PbResMatchPredictionTypePrediction
            ? PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
        logger.LogInformation("Match prediction of {TeamCount} teams by uid {Uid} refused: {Result}", wcsTeamList.Count, uid, result);
        return new pbSCMatchPredictionSelectRsp { ResultInfo = Result(result) };
    }

    public pbSCMatchPredictionTeamInfoRsp TeamInfo(long wcsTeamID) => new()
    {
        ResultInfo = Result(CurrentStatus() == PB_ENUM_RES_MATCH_PREDICTION_TYPE.PbResMatchPredictionTypeClose
            ? PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid),
        WcsTeamID = wcsTeamID,
    };

    public pbSCMatchPredictionRewardRsp Reward(ulong uid)
    {
        var result = CurrentStatus() != PB_ENUM_RES_MATCH_PREDICTION_TYPE.PbResMatchPredictionTypeAward
            ? PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed;
        logger.LogInformation("Match prediction reward of uid {Uid} refused: {Result}", uid, result);
        return new pbSCMatchPredictionRewardRsp { ResultInfo = Result(result) };
    }

    private PB_ENUM_RES_MATCH_PREDICTION_TYPE CurrentStatus()
    {
        var now = clock.GetUtcNow();
        if (schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeMatchPrediction, now) is null)
        {
            return PB_ENUM_RES_MATCH_PREDICTION_TYPE.PbResMatchPredictionTypeClose;
        }

        var nowSeconds = now.ToUnixTimeSeconds();
        var stage = gameData.PredictionTime.Rows.FirstOrDefault(row =>
            UTCTimeUtil.CfgTime2ServerUnixTime(row.StartTime) <= nowSeconds
            && nowSeconds <= UTCTimeUtil.CfgTime2ServerUnixTime(row.EndTime));
        return stage is null
            ? PB_ENUM_RES_MATCH_PREDICTION_TYPE.PbResMatchPredictionTypeClose
            : (PB_ENUM_RES_MATCH_PREDICTION_TYPE)stage.PredictionStatge;
    }

    private static pbResultInfo Result(PB_ENUM_RES_ERROR_CODE result) => new() { ResultId = (int)result };
}
