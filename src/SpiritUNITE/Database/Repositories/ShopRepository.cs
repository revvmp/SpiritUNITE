using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Shop;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed class ShopRepository(DataOptions options)
{
    private const int OneRow = 1;

    public async Task<IReadOnlyList<ShopBuyRecord>> GetBuyRecordsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var records = await database.GetTable<RoleShopBuy>()
            .Where(record => record.GameUlongUID == gameUlongUID)
            .ToArrayAsync();
        return [.. records.Select(record => new ShopBuyRecord(record.ShopID, record.SpecSaleNum, record.SpecSaleCycleTime))];
    }

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, RoleWallet? Wallet)> CommitPurchaseAsync(
        ulong gameUlongUID,
        ShopPurchase purchase)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        foreach (var (coinID, cost) in purchase.CoinCosts)
        {
            var debited = await database.GetTable<RoleCoin>()
                .Where(coin => coin.GameUlongUID == gameUlongUID && coin.CoinID == coinID && coin.CoinVal >= cost)
                .Set(coin => coin.CoinVal, coin => coin.CoinVal - cost)
                .UpdateAsync();
            if (debited != OneRow)
            {
                await transaction.RollbackAsync();
                return (PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin, null);
            }
        }

        try
        {
            foreach (var item in purchase.Items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }

            foreach (var pokemonId in purchase.PokemonIds)
            {
                await database.InsertAsync(new RolePokemon { GameUlongUID = gameUlongUID, PokemonId = pokemonId });
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrShopItemExist, null);
        }

        foreach (var record in purchase.BuyRecords)
        {
            await database.InsertOrReplaceAsync(new RoleShopBuy
            {
                GameUlongUID = gameUlongUID,
                ShopID = record.SpecSaleShopID,
                SpecSaleNum = record.SpecSaleNum,
                SpecSaleCycleTime = record.SpecSaleCycleTime,
            });
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet);
    }
}
