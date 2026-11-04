using Microsoft.Extensions.Options;
using SpiritUNITE.Database.Repositories;

namespace SpiritUNITE.Game.Wallet;

public sealed class WalletManager(WalletRepository repository, IOptions<WalletOptions> options)
{
    private readonly IReadOnlyDictionary<int, long> startingBalances = options.Value.StartingBalances();

    public Task<RoleWallet> LoadAsync(ulong gameUlongUID) => repository.LoadAsync(gameUlongUID, startingBalances);
}
