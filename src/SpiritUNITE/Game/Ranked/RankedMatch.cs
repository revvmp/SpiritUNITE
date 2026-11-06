using SpiritUNITE.Databin;
using SpiritUNITE.Tools;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Ranked;

public readonly record struct RankedMatchSeasonState(pbRankedMatchSeason Season, PB_ENUM_COM_RANK_STATE_TYPE RankedMatchState);

public static class RankedMatch
{
    public const PB_ENUM_RES_RANK_DIVISION StartingRankedDivision = PB_ENUM_RES_RANK_DIVISION.PbResRankDivisionBeginner5;
    public const int AllSeasonTaskAwards = -1;

    public static bool IsRankedMatchUnlocked(GameData gameData, LobbyNotices notices, LMNRoleInfo role) =>
        gameData.SystemUnlock.Find(pbSystemUnlock.Key(
            (int)PB_ENUM_RES_SYSTEM_UNLOCK_TYPE.PbResSystemUnlockTypeRank,
            notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeSystemUnlock))) is { } unlock
        && role.RoleTotalExp >= unlock.TrainerExp;

    public static int GetRankedMatchUnlockLevel(GameData gameData, LobbyNotices notices) =>
        gameData.SystemUnlock.Find(pbSystemUnlock.Key(
            (int)PB_ENUM_RES_SYSTEM_UNLOCK_TYPE.PbResSystemUnlockTypeRank,
            notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeSystemUnlock))) is { } unlock
            ? gameData.TrainerLevel.Rows.Where(row => row.UpgradeExp <= unlock.TrainerExp).Max(row => row.Level)
            : int.MaxValue;

    public static RankedMatchSeasonState FindCurrentSeason(GameData gameData, DateTimeOffset now)
    {
        var nowSeconds = now.ToUnixTimeSeconds();
        var seasons = gameData.RankedMatchSeason.Rows;
        var started = seasons
            .Where(season => UTCTimeUtil.CfgTime2ServerUnixTime(season.StartTime) <= nowSeconds)
            .MaxBy(season => UTCTimeUtil.CfgTime2ServerUnixTime(season.StartTime));

        if (started is null)
        {
            var first = seasons.MinBy(season => UTCTimeUtil.CfgTime2ServerUnixTime(season.StartTime))!;
            return new(first, PB_ENUM_COM_RANK_STATE_TYPE.PbComRankStateOfNull);
        }

        var state = nowSeconds <= UTCTimeUtil.CfgTime2ServerUnixTime(started.FinishTime)
            ? PB_ENUM_COM_RANK_STATE_TYPE.PbComRankStateOfSeasonprocess
            : PB_ENUM_COM_RANK_STATE_TYPE.PbComRankStateOfSeasonover;
        return new(started, state);
    }

    public static pbCSDT_RANKED_MATCH_DETAIL BuildStartingDetail(GameData gameData, DateTimeOffset now)
    {
        var current = FindCurrentSeason(gameData, now);
        return new pbCSDT_RANKED_MATCH_DETAIL
        {
            RankedMatchState = (int)current.RankedMatchState,
            CurrentRankedDivision = new pbCOMDT_RANKED_DIVISION
            {
                RankedDivision = (int)StartingRankedDivision,
                SeasonId = current.Season.SeasonId,
            },
            AwardedInfo = new pbCOMDT_RANKED_MATCH_AWARDED_INFO(),
        };
    }

    public static PB_ENUM_RES_ERROR_CODE RefuseSeasonTaskAward(int rankedDivision) =>
        rankedDivision == AllSeasonTaskAwards || Enum.IsDefined((PB_ENUM_RES_RANK_DIVISION)rankedDivision)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
}
