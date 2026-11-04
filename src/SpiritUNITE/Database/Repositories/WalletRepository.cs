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

    internal static Task CreditAsync(DataConnection database, ulong gameUlongUID, int coinID, long amount) =>
        database.GetTable<RoleCoin>().InsertOrUpdateAsync(
            () => new RoleCoin { GameUlongUID = gameUlongUID, CoinID = coinID, CoinVal = amount },
            saved => new RoleCoin { CoinVal = saved.CoinVal + amount });

    internal static async Task<bool> TryDebitAsync(DataConnection database, ulong gameUlongUID, int coinID, long amount) =>
        await database.GetTable<RoleCoin>()
            .Where(coin => coin.GameUlongUID == gameUlongUID && coin.CoinID == coinID && coin.CoinVal >= amount)
            .Set(coin => coin.CoinVal, coin => coin.CoinVal - amount)
            .UpdateAsync() == 1;

    internal static Task<long> ReadCoinValAsync(DataConnection database, ulong gameUlongUID, int coinID) =>
        database.GetTable<RoleCoin>()
            .Where(coin => coin.GameUlongUID == gameUlongUID && coin.CoinID == coinID)
            .Select(coin => coin.CoinVal)
            .FirstOrDefaultAsync();

    internal static async Task<RoleWallet> ReadAsync(DataConnection database, ulong gameUlongUID)
    {
        var balances = await database.GetTable<RoleCoin>()
            .Where(coin => coin.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(coin => coin.CoinID, coin => coin.CoinVal);
        return new RoleWallet(balances);
    }
}
