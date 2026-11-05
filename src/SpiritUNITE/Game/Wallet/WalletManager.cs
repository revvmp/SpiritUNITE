using Microsoft.Extensions.Options;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Wallet;

public sealed class WalletManager(WalletRepository repository, IOptions<WalletOptions> options)
{
    private readonly IReadOnlyDictionary<int, long> startingBalances = options.Value.StartingBalances();

    public Task<RoleWallet> LoadAsync(ulong gameUlongUID) => repository.LoadAsync(gameUlongUID, startingBalances);

    public async Task<GamePacket[]> GrantFirstLoginSpecialCoinsAsync(ulong gameUlongUID)
    {
        var grants = options.Value.FirstLoginSpecialCoins.ToDictionary(coin => coin.Key, coin => (long)coin.Value);
        if (grants.Count == 0)
        {
            return [];
        }

        var (beforeCoinNums, wallet) = await repository.CreditAsync(gameUlongUID, grants);
        return
        [
            .. grants.Keys.Select(coinID => GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, beforeCoinNums[coinID], wallet.GetCoinVal(coinID)))),
        ];
    }
}
