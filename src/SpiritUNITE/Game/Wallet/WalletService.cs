using Microsoft.Extensions.Options;

namespace SpiritUNITE.Game.Wallet;

public sealed class WalletService(IWalletRepository repository, IOptions<WalletOptions> options)
{
    private readonly IReadOnlyDictionary<int, long> startingBalances = options.Value.StartingBalances();

    public Task<RoleWallet> LoadAsync(ulong gameUlongUID) => repository.LoadAsync(gameUlongUID, startingBalances);
}
