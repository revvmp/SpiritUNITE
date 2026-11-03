namespace SpiritUNITE.Game.Data;

public sealed record pbStarRoadSite(
    int SiteID,
    IReadOnlyList<int> PokemonIDList,
    int SiteType = 0,
    int NeedScore = 0);
