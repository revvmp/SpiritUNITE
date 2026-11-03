namespace SpiritUNITE.Game.Wallet;

public interface IWalletRepository
{
    Task<RoleWallet> LoadAsync(ulong gameUlongUID, IReadOnlyDictionary<int, long> startingBalances);
}
