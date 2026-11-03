using SpiritUNITE.Game.Data;
using SpiritUNITE.Packets.Messages;
using static SpiritUNITE.Game.StarRoad.StarRoadConstants;

namespace SpiritUNITE.Game.StarRoad;

public sealed record StarRoadSitePokemon(int PokemonID, POKEMON_STATUS PokemonStatus);

public sealed record StarRoadSite(int SiteID, SITE_STATUS SiteStatus, IReadOnlyList<StarRoadSitePokemon> PokemonList);

public sealed class StarRoadProgress
{
    private const int LoginNtfFlag = 1;
    private const PB_ENUM_RES_ERROR_CODE NoGemPriceOffered = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
    private const PB_ENUM_RES_ERROR_CODE NoScoreEarned = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
    private const PB_ENUM_RES_ERROR_CODE NoCustomPokemonOffered = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
    private const PB_ENUM_RES_ERROR_CODE NoExclusiveScoreHeld = PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;

    private StarRoadProgress(StarRoadSite currentSite)
    {
        SiteList = [currentSite];
        CurSiteID = currentSite.SiteID;
    }

    public IReadOnlyList<StarRoadSite> SiteList { get; }
    public int CurSiteID { get; }

    public static StarRoadProgress Starting(GameData gameData)
    {
        var firstSite = gameData.StarRoadSite.Rows
            .Where(IsShownSite)
            .MinBy(row => row.SiteID)
            ?? throw new InvalidDataException(
                $"Game data table {gameData.StarRoadSite.TableName} has no normal or custom site");
        return new StarRoadProgress(new StarRoadSite(
            firstSite.SiteID,
            SITE_STATUS.BEGIN,
            FixedSitePokemon(firstSite)));
    }

    public PB_ENUM_RES_ERROR_CODE BuyScore(int siteId, int pokemonId)
    {
        var siteResult = CheckOpenSitePokemon(siteId, pokemonId);
        return siteResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? siteResult : NoGemPriceOffered;
    }

    public PB_ENUM_RES_ERROR_CODE ReceivePokemon(int siteId, int pokemonId)
    {
        var siteResult = CheckOpenSitePokemon(siteId, pokemonId);
        return siteResult != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc ? siteResult : NoScoreEarned;
    }

    public static PB_ENUM_RES_ERROR_CODE SelectCustomPokemon() => NoCustomPokemonOffered;

    public static PB_ENUM_RES_ERROR_CODE ExchangeExclusiveScore() => NoExclusiveScoreHeld;

    public pbCSDT_STAR_ROAD_INFO BuildStarRoadInfo(bool isLoginNtf)
    {
        var info = new pbCSDT_STAR_ROAD_INFO
        {
            SiteList = new pbCSDT_STAR_ROAD_SITE_LIST(),
            CurSiteID = CurSiteID,
            PokemonProgress = new pbCOMDT_STAR_ROAD_POKEMON_PROGRESS_LIST(),
            IsLoginNtf = isLoginNtf ? LoginNtfFlag : 0,
        };
        info.SiteList.SiteList.AddRange(SiteList.Select(BuildSiteInfo));
        return info;
    }

    private static pbCSDT_STAR_ROAD_SITE_INFO BuildSiteInfo(StarRoadSite site)
    {
        var siteInfo = new pbCSDT_STAR_ROAD_SITE_INFO
        {
            SiteID = site.SiteID,
            SiteStatus = (int)site.SiteStatus,
            SitePokemonList = new pbCSDT_STAR_ROAD_SITE_POKEMON_LIST(),
        };
        siteInfo.SitePokemonList.PokemonList.AddRange(site.PokemonList.Select(pokemon =>
            new pbCSDT_STAR_ROAD_SITE_POKEMON_INFO
            {
                PokemonID = pokemon.PokemonID,
                PokemonStatus = (int)pokemon.PokemonStatus,
                ScoreInfo = new pbCSDT_STAR_ROAD_SITE_POKEMON_SCORE_INFO { PokemonID = pokemon.PokemonID },
            }));
        return siteInfo;
    }

    private PB_ENUM_RES_ERROR_CODE CheckOpenSitePokemon(int siteId, int pokemonId)
    {
        var site = FindSite(siteId);
        if (site is null)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrStarRoadSiteIdoutRange;
        }

        if (site.SiteStatus == SITE_STATUS.END)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrStarRoadSiteAlreadyEnd;
        }

        return site.PokemonList.Any(pokemon => pokemon.PokemonID == pokemonId)
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
    }

    private StarRoadSite? FindSite(int siteId) => SiteList.FirstOrDefault(site => site.SiteID == siteId);

    private static bool IsShownSite(pbStarRoadSite row) =>
        row.SiteType is (int)PB_ENUM_RES_STAR_ROAD_SITE_TYPE.PbResStarRoadSiteTypeNormal
            or (int)PB_ENUM_RES_STAR_ROAD_SITE_TYPE.PbResStarRoadSiteTypeCustom;

    private static StarRoadSitePokemon[] FixedSitePokemon(pbStarRoadSite row) =>
        [.. row.PokemonIDList
            .Where(pokemonId => pokemonId > 0)
            .Select(pokemonId => new StarRoadSitePokemon(pokemonId, POKEMON_STATUS.NULL))];
}
