namespace SpiritUnite.Game.ServerContent;

public sealed class ServerListContent
{
    public int[] FreePokemonList { get; init; } = [];
    public int[] SubscriptionFreePokemonList { get; init; } = [];
    public int[] SubscriptionFreePasterList { get; init; } = [];
    public int[] NewSaleShopIDs { get; init; } = [];
}
