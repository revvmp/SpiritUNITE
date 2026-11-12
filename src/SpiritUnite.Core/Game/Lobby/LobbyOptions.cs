namespace SpiritUnite.Game.Lobby;

public sealed class LobbyOptions
{
    public const string SectionName = "LobbyRules";

    public int SubscriptionIsOpen { get; init; } = 1;
    public UserSchemeOptions[]? UserSchemes { get; init; }

    public bool IsValid() =>
        SubscriptionIsOpen is 0 or 1
        && (UserSchemes is null
            || (UserSchemes.All(scheme => scheme.BusinessType > 0 && scheme.SchemeId > 0)
                && UserSchemes.DistinctBy(scheme => scheme.BusinessType).Count() == UserSchemes.Length));
}

public sealed class UserSchemeOptions
{
    public int BusinessType { get; init; }
    public int SchemeId { get; init; }
}
