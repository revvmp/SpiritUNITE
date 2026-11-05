using System.Collections.Frozen;
using SpiritUNITE.Databin;
using SpiritUNITE.Tools;
using SpiritUNITE.Packets.Messages;
using static SpiritUNITE.Game.StarRoad.StarRoadConstants;

namespace SpiritUNITE.Game.StarRoad;

public sealed record StarRoadState(IReadOnlyDictionary<int, int> EndSites, int TipsSiteID)
{
    public static StarRoadState Empty { get; } = new(new Dictionary<int, int>(), NoSite);

    public const int NoSite = 0;

    public StarRoadState WithEndSite(int siteId, int curPokemonId) =>
        this with { EndSites = new Dictionary<int, int>(EndSites) { [siteId] = curPokemonId } };
}

public sealed record StarRoadSiteLayout(pbStarRoadSite Site, SITE_STATUS SiteStatus, int CurPokemonID, IReadOnlyList<int> Slots, int NeedScore)
{
    public const int EmptySlot = 0;

    public IEnumerable<int> PokemonIDs => Slots.Where(pokemonId => pokemonId != EmptySlot);
}

public sealed record StarRoadPool(IReadOnlyList<int> KnownPokemonIDs, int UnknownCount)
{
    public static StarRoadPool Empty { get; } = new([], 0);
}

public sealed class StarRoadRoute
{
    public const int FullProgress = 10000;
    public const int HalfProgress = FullProgress / 2;

    private const int Triggered = 1;

    private readonly IReadOnlyList<StarRoadSiteLayout> sites;
    private readonly pbStarRoadSite[] rows;
    private readonly StarRoadScores scores;

    private StarRoadRoute(
        pbStarRoadSite[] rows,
        StarRoadScores scores,
        IReadOnlyList<StarRoadSiteLayout> sites,
        StarRoadState state,
        long commonScoreNum)
    {
        State = state;
        this.rows = rows;
        this.scores = scores;
        this.sites = sites;
        CommonScoreNum = commonScoreNum;
        CurSite = sites.FirstOrDefault(site => site.SiteStatus == SITE_STATUS.BEGIN);
    }

    public StarRoadState State { get; }

    public StarRoadSiteLayout? CurSite { get; }

    public long CommonScoreNum { get; }

    public int CurSiteID => CurSite?.Site.SiteID ?? StarRoadState.NoSite;

    public bool CanReceiveAny => CurSite is { } site && site.PokemonIDs.Any() && CommonScoreNum >= site.NeedScore;

    public int Progress => CurSite is not { NeedScore: > 0 } site
        ? 0
        : (int)Math.Min(FullProgress, CommonScoreNum * FullProgress / site.NeedScore);

    public bool ShowsProgress => CurSite is { } site && site.PokemonIDs.Any() && Progress >= HalfProgress;

    public static StarRoadRoute Build(GameData gameData, StarRoadScores scores, StarRoadState state, long commonScoreNum, DateTimeOffset now)
    {
        var rows = gameData.StarRoadSite.Rows.OrderBy(row => row.SiteID).ToArray();
        var movedOut = rows
            .Where(row => IsExtraSite(row) && state.EndSites.ContainsKey(row.SiteID))
            .Select(row => state.EndSites[row.SiteID])
            .ToHashSet();
        var nowSeconds = now.ToUnixTimeSeconds();
        var pools = new Dictionary<int, StarRoadPool>();
        var sites = new List<StarRoadSiteLayout>();
        var curFound = false;
        var lastRouteNeedScore = 0;
        foreach (var row in rows)
        {
            if (IsExtraSite(row))
            {
                if (state.EndSites.TryGetValue(row.SiteID, out var heldPokemonId))
                {
                    sites.Add(new StarRoadSiteLayout(row, SITE_STATUS.END, heldPokemonId, [heldPokemonId], lastRouteNeedScore));
                }

                continue;
            }

            if (!IsRouteSite(row) || !IsOnTime(row, nowSeconds))
            {
                continue;
            }

            lastRouteNeedScore = row.NeedScore;
            var hasCurPokemon = state.EndSites.TryGetValue(row.SiteID, out var curPokemonId);
            var slots = row.SiteType == (int)PB_ENUM_RES_STAR_ROAD_SITE_TYPE.PbResStarRoadSiteTypeCustom
                ? [.. Enumerable.Repeat(StarRoadSiteLayout.EmptySlot, row.SlotNum)]
                : TakeFromPool(pools, row, movedOut, hasCurPokemon ? curPokemonId : null);
            if (slots.Length == 0)
            {
                continue;
            }

            if (hasCurPokemon)
            {
                sites.Add(new StarRoadSiteLayout(row, SITE_STATUS.END, curPokemonId, slots, row.NeedScore));
            }
            else
            {
                var status = curFound ? SITE_STATUS.NULL : SITE_STATUS.BEGIN;
                curFound = true;
                sites.Add(new StarRoadSiteLayout(row, status, 0, slots, row.NeedScore));
            }
        }

        return new StarRoadRoute(rows, scores, sites, state, commonScoreNum);
    }

    private static int[] TakeFromPool(Dictionary<int, StarRoadPool> pools, pbStarRoadSite row, HashSet<int> movedOut, int? curPokemonId)
    {
        var pool = pools.GetValueOrDefault(row.NeedScore) ?? StarRoadPool.Empty;
        int[] known = [.. row.PokemonIDList.Where(pokemonId => pokemonId > 0), .. pool.KnownPokemonIDs];
        var shownCount = Math.Min(row.SlotNum, known.Length + pool.UnknownCount);
        int[] shown = [.. known.Concat(Enumerable.Repeat(StarRoadSiteLayout.EmptySlot, pool.UnknownCount)).Take(shownCount)];
        var remaining = known.Where(pokemonId => !movedOut.Contains(pokemonId)).ToArray();
        pools[row.NeedScore] = curPokemonId is { } chosen
            ? new StarRoadPool([.. remaining.Where(pokemonId => pokemonId != chosen)], pool.UnknownCount)
            : new StarRoadPool([], Math.Max(0, remaining.Length + pool.UnknownCount - 1));
        return [.. shown.Where(pokemonId => !movedOut.Contains(pokemonId))];
    }

    public StarRoadSiteLayout? FindSite(int siteId) => sites.FirstOrDefault(site => site.Site.SiteID == siteId);

    public StarRoadSiteLayout? FindHoldingSite(int pokemonId) =>
        (CurSite is { } cur && cur.PokemonIDs.Contains(pokemonId) ? cur : null)
        ?? sites.FirstOrDefault(site => site.SiteStatus == SITE_STATUS.NULL && IsRouteSite(site.Site) && site.PokemonIDs.Contains(pokemonId))
        ?? sites.FirstOrDefault(site => site.SiteStatus == SITE_STATUS.END && IsRouteSite(site.Site) && site.PokemonIDs.Contains(pokemonId));

    public int? FindFreeExtraSite(int routeSiteId)
    {
        var index = Array.FindIndex(rows, row => row.SiteID == routeSiteId);
        for (var next = index + 1; index >= 0 && next < rows.Length && IsExtraSite(rows[next]); next++)
        {
            if (!State.EndSites.ContainsKey(rows[next].SiteID))
            {
                return rows[next].SiteID;
            }
        }

        return null;
    }

    public pbCSDT_STAR_ROAD_INFO BuildStarRoadInfo(bool isLoginNtf, bool triggerTips)
    {
        var info = new pbCSDT_STAR_ROAD_INFO
        {
            SiteList = new pbCSDT_STAR_ROAD_SITE_LIST
            {
                SiteList = { sites.Select(site => BuildSiteInfo(site, site.SiteStatus == SITE_STATUS.BEGIN)) },
            },
            CurSiteID = CurSiteID,
            CurSitePokemonScoreList = new pbCSDT_STAR_ROAD_POKEMON_SCORE_LIST(),
            IsLoginNtf = isLoginNtf ? Triggered : 0,
            TriggerTips = triggerTips ? Triggered : 0,
        };
        if (ShowsProgress)
        {
            info.PokemonProgress = new pbCOMDT_STAR_ROAD_POKEMON_PROGRESS_LIST
            {
                PokemonProgressList =
                {
                    CurSite!.PokemonIDs.OrderByDescending(pokemonId => pokemonId).Select(pokemonId =>
                        new pbCOMDT_STAR_ROAD_POKEMON_PROGRESS_INFO
                        {
                            PokemonID = pokemonId,
                            Progress = Progress,
                            IsFirstTriggered = Triggered,
                        }),
                },
            };
        }

        return info;
    }

    public pbSCStarRoadSiteUpdateNtf BuildSiteUpdateNtf(int siteId)
    {
        var site = FindSite(siteId) ?? throw new InvalidOperationException($"License Journey site {siteId} is not shown");
        return new pbSCStarRoadSiteUpdateNtf { SiteInfo = BuildSiteInfo(site, IsRouteSite(site.Site)) };
    }

    public static bool IsRouteSite(pbStarRoadSite row) =>
        row.SiteType is (int)PB_ENUM_RES_STAR_ROAD_SITE_TYPE.PbResStarRoadSiteTypeNormal
            or (int)PB_ENUM_RES_STAR_ROAD_SITE_TYPE.PbResStarRoadSiteTypeCustom;

    private static bool IsExtraSite(pbStarRoadSite row) =>
        row.SiteType == (int)PB_ENUM_RES_STAR_ROAD_SITE_TYPE.PbResStarRoadSiteTypeHide;

    private static bool IsOnTime(pbStarRoadSite row, long nowSeconds) =>
        row.OnTime == 0 || UTCTimeUtil.CfgTime2ServerUnixTime(row.OnTime) <= nowSeconds;

    private pbCSDT_STAR_ROAD_SITE_INFO BuildSiteInfo(StarRoadSiteLayout site, bool withCommonScore) => new()
    {
        SiteID = site.Site.SiteID,
        SiteStatus = (int)site.SiteStatus,
        SitePokemonList = new pbCSDT_STAR_ROAD_SITE_POKEMON_LIST
        {
            PokemonList = { site.Slots.Select(pokemonId => BuildSitePokemonInfo(pokemonId, site.NeedScore, withCommonScore)) },
        },
        CurPokemonID = site.CurPokemonID,
    };

    private pbCSDT_STAR_ROAD_SITE_POKEMON_INFO BuildSitePokemonInfo(int pokemonId, int needScore, bool withCommonScore)
    {
        if (pokemonId == StarRoadSiteLayout.EmptySlot)
        {
            return new pbCSDT_STAR_ROAD_SITE_POKEMON_INFO();
        }

        var commonScoreNum = withCommonScore ? CommonScoreNum : 0;
        var scoreInfo = scores.BuildScoreInfo(pokemonId, needScore, commonScoreNum);
        return new pbCSDT_STAR_ROAD_SITE_POKEMON_INFO
        {
            PokemonID = pokemonId,
            PokemonStatus = commonScoreNum >= needScore && needScore > 0 ? (int)POKEMON_STATUS.CAN_RECEIVE : (int)POKEMON_STATUS.NULL,
            ScoreInfo = scoreInfo,
        };
    }
}

public sealed class StarRoadScores
{
    private readonly FrozenDictionary<int, int> couponsByPokemon;
    private readonly FrozenDictionary<int, int> exclusiveScoreIdByPokemon;

    public StarRoadScores(GameData gameData)
    {
        couponsByPokemon = gameData.StarRoadPokemon.Rows.ToFrozenDictionary(row => row.PokemonId, row => row.PokemonCoupons);
        exclusiveScoreIdByPokemon = gameData.OutSideItemBase.Rows
            .Where(item => item.OutSideItemType == (int)PB_ENUM_RES_OUTSIDEITEM_TYPE.PbResOutsideitemTypeExclusiveStarRoadScore
                && item.ItemParam.Count > 0)
            .GroupBy(item => item.ItemParam[0])
            .ToFrozenDictionary(group => group.Key, group => group.Min(item => item.OutSideItemId));
    }

    public pbCSDT_STAR_ROAD_SITE_POKEMON_SCORE_INFO BuildScoreInfo(int pokemonId, int needScore, long commonScoreNum)
    {
        var coupons = couponsByPokemon.GetValueOrDefault(pokemonId);
        var scoreInfo = new pbCSDT_STAR_ROAD_SITE_POKEMON_SCORE_INFO
        {
            PokemonID = pokemonId,
            ExclusiveScoreID = exclusiveScoreIdByPokemon.GetValueOrDefault(pokemonId),
        };
        if (commonScoreNum >= needScore)
        {
            scoreInfo.CommonScoreNum = (int)commonScoreNum;
            scoreInfo.DelCommonScoreNum = needScore;
            return scoreInfo;
        }

        var missing = needScore - (int)commonScoreNum;
        scoreInfo.CommonScoreNum = (int)commonScoreNum;
        scoreInfo.DelCommonScoreNum = (int)commonScoreNum;
        scoreInfo.BuyScoreNum = missing;
        scoreInfo.PokemonCoupons = needScore > 0 ? (int)((long)coupons * missing / needScore) : 0;
        return scoreInfo;
    }
}
