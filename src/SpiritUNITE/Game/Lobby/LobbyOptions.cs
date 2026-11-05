namespace SpiritUNITE.Game.Lobby;

public sealed class LobbyOptions
{
    public const string SectionName = "LobbyRules";

    public CompetitionOpenOptions CompetitionOpen { get; init; } = new();
    public int StartingCreditPoint { get; init; }
    public int SubscriptionIsOpen { get; init; }
    public int[] NewSaleShopIDs { get; init; } = [];
    public UserSchemeOptions[] UserSchemes { get; init; } = [];
    public SubscriptionFreeOptions SubscriptionFree { get; init; } = new();

    public bool IsValid() =>
        CompetitionOpen.BeginTime <= CompetitionOpen.EndTime
        && CompetitionOpen.IsOpen is 0 or 1
        && StartingCreditPoint >= 0
        && SubscriptionIsOpen is 0 or 1
        && NewSaleShopIDs.All(shopID => shopID > 0)
        && UserSchemes.All(scheme => scheme.BusinessType > 0 && scheme.SchemeId > 0)
        && UserSchemes.DistinctBy(scheme => scheme.BusinessType).Count() == UserSchemes.Length
        && SubscriptionFree.FreePokemonList.All(pokemonID => pokemonID > 0)
        && SubscriptionFree.FreePasterList.All(pasterID => pasterID > 0);
}

public sealed class CompetitionOpenOptions
{
    public int IsOpen { get; init; }
    public long BeginTime { get; init; }
    public long EndTime { get; init; }
}

public sealed class SubscriptionFreeOptions
{
    public int[] FreePokemonList { get; init; } = [];
    public int[] FreePasterList { get; init; } = [];
}

public sealed class UserSchemeOptions
{
    public int BusinessType { get; init; }
    public int SchemeId { get; init; }
}
