using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Wallet;

namespace SpiritUNITE.Database.Repositories;

public sealed class WalletRepository(DataOptions options)
{
    public async Task<RoleWallet> LoadAsync(ulong gameUlongUID, IReadOnlyDictionary<int, long> startingBalances)
    {
        await using var database = new DataConnection(options);
        var savedCoinIDs = await database.GetTable<RoleCoin>()
            .Where(coin => coin.GameUlongUID == gameUlongUID)
            .Select(coin => coin.CoinID)
            .ToArrayAsync();
        foreach (var (coinID, startingCoinVal) in startingBalances.Where(balance => !savedCoinIDs.Contains(balance.Key)))
        {
            try
            {
                await database.InsertAsync(new RoleCoin
                {
                    GameUlongUID = gameUlongUID,
                    CoinID = coinID,
                    CoinVal = startingCoinVal,
                });
            }
            catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
            }
        }

        return await ReadAsync(database, gameUlongUID);
    }

    internal static async Task<RoleWallet> ReadAsync(DataConnection database, ulong gameUlongUID)
    {
        var balances = await database.GetTable<RoleCoin>()
            .Where(coin => coin.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(coin => coin.CoinID, coin => coin.CoinVal);
        return new RoleWallet(balances);
    }
}
