using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Profile;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Ranked;

public sealed class RankedRankingBoard(
    RankedRepository repository,
    RankedManager ranked,
    RoleInfoStore roles,
    ProfileManager profiles,
    GameData gameData)
{
    public const int ScoreTypeRankedDivision = (int)PB_ENUM_COM_APOLLO_TRANK_SCORE_TYPE.PbComApolloTrankScoreTypeRankedDivision;

    private const int TotalRankCount = 9999;
    private const int FirstRankNo = 1;
    private const int MaxLimit = 100;
    private const int SeasonScoreScale = 1000000;
    private const int IndexScoreScale = 10000;

    public async Task<pbSCGetRankingListRsp> BuildRankingListAsync(pbCOMDT_RANKING_BASE_INFO query)
    {
        var start = Math.Max(query.Start, FirstRankNo);
        var limit = Math.Clamp(query.Limit, 0, MaxLimit);
        var seasonId = ranked.CurrentSeasonId;
        var ranking = await RankAsync(seasonId);
        var list = new pbCOMDT_RANKING_LIST_SUCC();
        foreach (var (entry, index) in ranking.Select((entry, index) => (entry, index)).Skip(start - FirstRankNo).Take(limit))
        {
            if (await roles.FindAsync(entry.GameUlongUID) is not { } role)
            {
                continue;
            }

            list.ItemDetail.Add(new pbCOMDT_RANKING_LIST_ITEM_INFO
            {
                OpenID = entry.GameUlongUID.ToString(),
                RankNo = index + FirstRankNo,
                RankScore = RankScoreOf(seasonId, entry.Position),
                ExtraInfo = await BuildExtraInfoAsync(role, entry.Position, seasonId),
            });
        }

        return new pbSCGetRankingListRsp
        {
            ResultInfo = new pbResultInfo(),
            TopBaseInfo = new pbCOMDT_RANKING_BASE_INFO { ScoreType = ScoreTypeRankedDivision, Start = start, Limit = limit },
            RankingList = list,
        };
    }

    public async Task<pbCOMDT_ACNT_RANKING_DETAIL> BuildAcntRankingDetailAsync(LMNRoleInfo role)
    {
        var uid = role.GameUlongUID;
        var seasonId = ranked.CurrentSeasonId;
        var season = (await ranked.LoadAsync(uid)).Season;
        var placed = season.BattleCnt > 0;
        var position = placed ? season.Position : RankedLadder.UnplacedPosition;
        var item = new pbCOMDT_ACNT_RANKING_ITEM_INFO
        {
            ExtraInfo = await BuildExtraInfoAsync(role, position, seasonId),
            TotalRankCount = TotalRankCount,
        };
        if (placed && (await RankAsync(seasonId)).ToList().FindIndex(entry => entry.GameUlongUID == uid) is var index and >= 0 and < TotalRankCount)
        {
            item.RankNo = index + FirstRankNo;
            item.RankScore = RankScoreOf(seasonId, position);
        }

        return new pbCOMDT_ACNT_RANKING_DETAIL
        {
            ScoreType = ScoreTypeRankedDivision,
            ItemDetail = { item },
        };
    }

    private async Task<IEnumerable<RankedSeasonRanking>> RankAsync(int seasonId) =>
        (await repository.ListPlacedSeasonAsync(seasonId))
            .OrderByDescending(entry => RankScoreOf(seasonId, entry.Position))
            .ThenBy(entry => entry.GameUlongUID);

    private int RankScoreOf(int seasonId, RankedPosition position)
    {
        var shown = ranked.Ladder.ToClientPosition(position);
        return seasonId * SeasonScoreScale
            + ranked.Ladder.IndexOf(shown.RankedDivision) * IndexScoreScale
            + (ranked.Ladder.IsMaster(shown.RankedDivision) ? shown.MasterPoint : shown.WinPoint);
    }

    private async Task<pbCOMDT_RANKING_ITEM_EXTRA_INFO> BuildExtraInfoAsync(LMNRoleInfo role, RankedPosition position, int seasonId)
    {
        var style = await profiles.LoadAsync(role.GameUlongUID);
        return new pbCOMDT_RANKING_ITEM_EXTRA_INFO
        {
            ScoreType = ScoreTypeRankedDivision,
            DetailInfo = new pbCOMDT_RANKING_ITEM_EXTRA_DETAIL
            {
                RankedDivision = new pbCOMDT_RANKING_LIST_ITEM_EXTRA_RANKED_DIVISION
                {
                    BaseInfo = new pbCOMDT_RANKING_LIST_ITEM_EXTRA_BASE
                    {
                        RoleLevel = RoleProfile.GetLevel(gameData, role),
                        Uid = role.GameUlongUID,
                        RoleName = role.InGameUsrName,
                        RoleGender = role.Gender,
                        HeadID = RoleImage.GetHeadID(gameData, role),
                        CurHeadIconIDInfo = profiles.BuildHeadIconInfo(role, style).CurHeadIconInfo,
                        RoleAvatarInfo = role.RoleAvatarInfo.Clone(),
                        PrivilePlatInfo = new pbCOMDT_PRIVILE_PLAT_INFO(),
                        UserPlantInfo = style.BuildUserPlantInfo(),
                    },
                    RankedDivision = ranked.ToDivision(position, seasonId),
                },
            },
        };
    }
}
