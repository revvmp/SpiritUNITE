using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public static class ActivityDropRewards
{
    public static ActivityTaskGrant Merge(IReadOnlyCollection<ActivityTaskGrant> grants) => new(
        [.. grants.SelectMany(grant => grant.Items)],
        [.. grants.SelectMany(grant => grant.PokemonIds)],
        new BagChange
        {
            Grants = [.. grants.SelectMany(grant => grant.Change.Grants)],
            CoinGrants = grants
                .SelectMany(grant => grant.Change.CoinGrants)
                .GroupBy(coin => coin.Key)
                .ToDictionary(group => group.Key, group => group.Sum(coin => coin.Value)),
        },
        [.. grants.SelectMany(grant => grant.Awards)],
        grants
            .SelectMany(grant => grant.AddedResources)
            .GroupBy(resource => resource.Key)
            .ToDictionary(group => group.Key, group => group.Sum(resource => resource.Value)));

    public static BagChange ToBagChange(ActivityTaskGrant grant, PB_ENUM_ENM_RESOURCE_CHG_REASON reason, long rewardTime, BagChange costs) =>
        costs with
        {
            Grants = [.. costs.Grants, .. grant.Change.Grants],
            CoinGrants = costs.CoinGrants
                .Concat(grant.Change.CoinGrants)
                .GroupBy(coin => coin.Key)
                .ToDictionary(group => group.Key, group => group.Sum(coin => coin.Value)),
            PokemonGrants =
            [
                .. costs.PokemonGrants,
                .. grant.PokemonIds.Select(pokemonId => new RolePokemonInfo(pokemonId, rewardTime, (int)reason, RolePokemonInfo.HasNewTag)),
            ],
            ItemGrants = [.. costs.ItemGrants, .. grant.Items],
        };

    public static ActivityRewardGroup ToRewardGroup(ActivityTaskGrant grant, BagChange change, ActivityRewardCommit commit) =>
        new(
            commit.Wallet!,
            [.. change.CoinGrants.Keys.Union(change.CoinCosts.Keys)],
            commit.SpecialCoinsBefore,
            commit.ChangedItems,
            grant.PokemonIds,
            grant.Awards)
        {
            Items = grant.Items,
        };
}
