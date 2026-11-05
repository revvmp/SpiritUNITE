using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;

namespace SpiritUNITE.Game.Bag;

public sealed record BagGrant(RoleItemType ItemType, int ItemId, int Cnt, int StackSize, int InitialLevel, int NewTag = 0);

public sealed record BagCost(RoleItemType ItemType, int ItemId, int Cnt);

public sealed record BagLevelUp(int ItemId, int FromLevel);

public sealed record BagChange
{
    public IReadOnlyList<BagCost> Costs { get; init; } = [];
    public IReadOnlyList<BagGrant> Grants { get; init; } = [];
    public BagLevelUp? LevelUp { get; init; }
    public IReadOnlyDictionary<int, long> CoinCosts { get; init; } = new Dictionary<int, long>();
    public IReadOnlyDictionary<int, long> CoinGrants { get; init; } = new Dictionary<int, long>();
    public IReadOnlyList<RolePokemonInfo> PokemonGrants { get; init; } = [];

    public bool ChangesCoins => CoinCosts.Count > 0 || CoinGrants.Count > 0;

    public IEnumerable<(RoleItemType ItemType, int ItemId)> TouchedItems =>
        Costs.Select(cost => (cost.ItemType, cost.ItemId))
            .Concat(Grants.Select(grant => (grant.ItemType, grant.ItemId)))
            .Concat(LevelUp is { } levelUp ? [(RoleItemType.OutSideItem, levelUp.ItemId)] : [])
            .Distinct();
}
