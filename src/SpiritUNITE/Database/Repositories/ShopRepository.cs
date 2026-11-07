using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
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

    public async Task<(PB_ENUM_RES_ERROR_CODE Result, RoleWallet? Wallet, IReadOnlyList<BagItem> ChangedBagItems)> CommitPurchaseAsync(
        ulong gameUlongUID,
        ShopPurchase purchase,
        long now)
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
                return (PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin, null, []);
            }
        }

        var itemsPaid = await BagRepository.ApplyAsync(database, gameUlongUID, new BagChange { Costs = purchase.ItemCosts }, now);
        if (itemsPaid != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrOutSideItemFragmentNotEnough, null, []);
        }

        try
        {
            foreach (var item in purchase.Items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }

            foreach (var pokemonId in purchase.PokemonIds)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, new RolePokemonInfo(
                    pokemonId,
                    now,
                    (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonShopadditem,
                    RolePokemonInfo.HasNewTag)));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return (PB_ENUM_RES_ERROR_CODE.PbErrShopItemExist, null, []);
        }

        var granted = await BagRepository.ApplyGrantsAsync(database, gameUlongUID, purchase.OutSideItems, now);
        if (granted != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return (granted, null, []);
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
        var changedBagItems = await BagRepository.ReadItemsAsync(
            database,
            gameUlongUID,
            purchase.OutSideItems.Select(grant => (grant.ItemType, grant.ItemId))
                .Concat(purchase.ItemCosts.Select(cost => (cost.ItemType, cost.ItemId)))
                .Distinct());
        await transaction.CommitAsync();
        return (PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet, changedBagItems);
    }
}
