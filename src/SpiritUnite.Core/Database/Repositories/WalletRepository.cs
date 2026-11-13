using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Wallet;

namespace SpiritUnite.Database.Repositories;

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

    public async Task<RoleWallet> FindAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await ReadAsync(database, gameUlongUID);
    }

    public async Task<(IReadOnlyDictionary<int, long> BeforeCoinNums, RoleWallet Wallet)> CreditAsync(
        ulong gameUlongUID,
        IReadOnlyDictionary<int, long> grants)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var (coinID, amount) in grants)
        {
            beforeCoinNums[coinID] = await ReadCoinValAsync(database, gameUlongUID, coinID);
            await CreditAsync(database, gameUlongUID, coinID, amount);
        }

        var wallet = await ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return (beforeCoinNums, wallet);
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
        var balancesInGrantOrder = await database.GetTable<RoleCoin>()
            .Where(coin => coin.GameUlongUID == gameUlongUID)
            .OrderBy(coin => coin.CoinSeq)
            .ToArrayAsync();
        return new RoleWallet([.. balancesInGrantOrder.Select(coin => KeyValuePair.Create(coin.CoinID, coin.CoinVal))]);
    }
}
