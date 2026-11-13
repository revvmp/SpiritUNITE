using SpiritUnite.Game.Energy;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.PokemonAvatar;
using SpiritUnite.Game.Role;

namespace SpiritUnite.Game.Bag;

public sealed record BagGrant(RoleItemType ItemType, int ItemId, int Cnt, int StackSize, int InitialLevel, int NewTag = 0)
{
    public EnergyAccelerator? Accelerator { get; init; }

    public bool InstallsIntoEnergyCenter => Accelerator is not null;
}

public sealed record BagCost(RoleItemType ItemType, int ItemId, int Cnt);

public sealed record BagLevelUp(int ItemId, int FromLevel, int ToLevel);

public sealed record BagHeldItemExperience(int ItemId, int Level, long ExpireTime);

public sealed record BagChange
{
    public IReadOnlyList<BagCost> Costs { get; init; } = [];
    public IReadOnlyList<BagGrant> Grants { get; init; } = [];
    public BagLevelUp? LevelUp { get; init; }
    public BagHeldItemExperience? HeldItemExperience { get; init; }
    public IReadOnlyDictionary<int, long> CoinCosts { get; init; } = new Dictionary<int, long>();
    public IReadOnlyDictionary<int, long> CoinGrants { get; init; } = new Dictionary<int, long>();
    public IReadOnlyList<RolePokemonInfo> PokemonGrants { get; init; } = [];
    public IReadOnlyList<RoleItemInfo> ItemGrants { get; init; } = [];
    public IReadOnlyList<PasterTimeLimit> PasterTimeLimits { get; init; } = [];
    public IReadOnlyList<RolePokemonInfo> TimeLimitedPokemon { get; init; } = [];

    public bool ChangesCoins => CoinCosts.Count > 0 || CoinGrants.Count > 0;

    public IEnumerable<(RoleItemType ItemType, int ItemId)> TouchedItems =>
        Costs.Select(cost => (cost.ItemType, cost.ItemId))
            .Concat(Grants.Select(grant => (grant.ItemType, grant.ItemId)))
            .Concat(LevelUp is { } levelUp ? [(RoleItemType.OutSideItem, levelUp.ItemId)] : [])
            .Concat(HeldItemExperience is { } experience ? [(RoleItemType.OutSideItem, experience.ItemId)] : [])
            .Distinct();
}
