using Microsoft.Extensions.Options;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Wallet;

public sealed class WalletManager(WalletRepository repository, GameData gameData, IOptionsMonitor<WalletOptions> options)
{
    public Task<RoleWallet> LoadAsync(ulong gameUlongUID) => repository.LoadAsync(gameUlongUID, options.CurrentValue.StartingBalances());

    public async Task<GamePacket[]> GrantFirstLoginSpecialCoinsAsync(ulong gameUlongUID)
    {
        var grants = NewPlayerDefaultGrants.RowsOf(gameData, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesResattr)
            .Where(row => CoinID.IsSpecialCoin(row.ResourceId))
            .GroupBy(row => row.ResourceId)
            .ToDictionary(coin => coin.Key, coin => coin.Sum(row => (long)row.ResourceNum));
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
