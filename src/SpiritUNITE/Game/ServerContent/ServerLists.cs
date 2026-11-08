using Microsoft.Extensions.Hosting;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;

namespace SpiritUNITE.Game.ServerContent;

public sealed class ServerLists(ServerContentRepository repository, GameData gameData) : IHostedService
{
    public const int MaxListLength = 500;

    private readonly SemaphoreSlim saveGate = new(1, 1);
    private volatile ServerListContent current = new();

    public ServerListContent Current => current;

    public async Task StartAsync(CancellationToken cancellationToken) => current = FromLists(await repository.LoadListsAsync());

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public bool IsValid(ServerListContent content) =>
        IsIdList(content.FreePokemonList, id => gameData.PokemonBase.Contains(id))
        && IsIdList(content.SubscriptionFreePokemonList, id => gameData.PokemonBase.Contains(id))
        && IsIdList(content.SubscriptionFreePasterList, id => gameData.PokemonAvatarBase.Contains(id))
        && IsIdList(content.NewSaleShopIDs, id => gameData.Shop.Contains(id));

    public async Task<bool> ReplaceAsync(ServerListContent content)
    {
        if (!IsValid(content))
        {
            return false;
        }

        await saveGate.WaitAsync();
        try
        {
            await repository.ReplaceListsAsync(ToLists(content));
            current = content;
            return true;
        }
        finally
        {
            saveGate.Release();
        }
    }

    private static bool IsIdList(int[] ids, Func<int, bool> exists) =>
        ids.Length <= MaxListLength && ids.Distinct().Count() == ids.Length && ids.All(exists);

    private static ServerListContent FromLists(Dictionary<string, int[]> lists) => new()
    {
        FreePokemonList = lists.GetValueOrDefault(nameof(ServerListContent.FreePokemonList)) ?? [],
        SubscriptionFreePokemonList = lists.GetValueOrDefault(nameof(ServerListContent.SubscriptionFreePokemonList)) ?? [],
        SubscriptionFreePasterList = lists.GetValueOrDefault(nameof(ServerListContent.SubscriptionFreePasterList)) ?? [],
        NewSaleShopIDs = lists.GetValueOrDefault(nameof(ServerListContent.NewSaleShopIDs)) ?? [],
    };

    private static Dictionary<string, int[]> ToLists(ServerListContent content) => new(StringComparer.Ordinal)
    {
        [nameof(ServerListContent.FreePokemonList)] = content.FreePokemonList,
        [nameof(ServerListContent.SubscriptionFreePokemonList)] = content.SubscriptionFreePokemonList,
        [nameof(ServerListContent.SubscriptionFreePasterList)] = content.SubscriptionFreePasterList,
        [nameof(ServerListContent.NewSaleShopIDs)] = content.NewSaleShopIDs,
    };
}
