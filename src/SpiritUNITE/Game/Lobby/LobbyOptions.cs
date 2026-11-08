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
    public UserSchemeOptions[]? UserSchemes { get; init; }

    public IReadOnlyList<UserSchemeOptions> UserSchemesOrOfficial => UserSchemes ?? OfficialUserSchemes;

    public bool IsValid() =>
        SubscriptionIsOpen is 0 or 1
        && UserSchemesOrOfficial.All(scheme => scheme.BusinessType > 0 && scheme.SchemeId > 0)
        && UserSchemesOrOfficial.DistinctBy(scheme => scheme.BusinessType).Count() == UserSchemesOrOfficial.Count;
}

public sealed class UserSchemeOptions
{
    public int BusinessType { get; init; }
    public int SchemeId { get; init; }
}
