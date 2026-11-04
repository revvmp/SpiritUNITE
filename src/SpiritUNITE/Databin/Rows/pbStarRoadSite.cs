namespace SpiritUNITE.Databin;

public sealed record pbStarRoadSite(
    int SiteID,
    IReadOnlyList<int> PokemonIDList,
    int SiteType = 0,
    int NeedScore = 0,
    int SlotNum = 0,
    long OnTime = 0);
