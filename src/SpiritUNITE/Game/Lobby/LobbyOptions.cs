namespace SpiritUNITE.Game.Lobby;

public sealed class LobbyOptions
{
    public const string SectionName = "LobbyRules";

    private static readonly UserSchemeOptions[] OfficialUserSchemes =
    [
        new() { BusinessType = 1, SchemeId = 4 },
        new() { BusinessType = 2, SchemeId = 5 },
        new() { BusinessType = 4, SchemeId = 3 },
        new() { BusinessType = 5, SchemeId = 2 },
        new() { BusinessType = 6, SchemeId = 2 },
        new() { BusinessType = 7, SchemeId = 2 },
        new() { BusinessType = 3, SchemeId = 1 },
    ];

    public int SubscriptionIsOpen { get; init; } = 1;
    public int[] NewSaleShopIDs { get; init; } = [];
    public UserSchemeOptions[]? UserSchemes { get; init; }
    public SubscriptionFreeOptions SubscriptionFree { get; init; } = new();

    public IReadOnlyList<UserSchemeOptions> UserSchemesOrOfficial => UserSchemes ?? OfficialUserSchemes;

    public bool IsValid() =>
        SubscriptionIsOpen is 0 or 1
        && NewSaleShopIDs.All(shopID => shopID > 0)
        && UserSchemesOrOfficial.All(scheme => scheme.BusinessType > 0 && scheme.SchemeId > 0)
        && UserSchemesOrOfficial.DistinctBy(scheme => scheme.BusinessType).Count() == UserSchemesOrOfficial.Count
        && SubscriptionFree.FreePokemonList.All(pokemonID => pokemonID > 0)
        && SubscriptionFree.FreePasterList.All(pasterID => pasterID > 0);
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
