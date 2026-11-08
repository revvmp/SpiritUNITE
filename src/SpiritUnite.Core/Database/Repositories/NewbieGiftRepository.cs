using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Newbie;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed record NewbieGiftCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    RoleWallet? Wallet,
    IReadOnlyDictionary<int, long> BeforeCoinNums);

public sealed class NewbieGiftRepository(DataOptions options)
{
    public async Task<IReadOnlySet<int>> LoadFetchedGiftIDsAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        var giftIDs = await database.GetTable<RoleNewbieGift>()
            .Where(gift => gift.GameUlongUID == gameUlongUID)
            .Select(gift => gift.GiftID)
            .ToArrayAsync();
        return giftIDs.ToHashSet();
    }

    public async Task<NewbieGiftCommit> FetchAsync(ulong gameUlongUID, NewbieGiftGrant grant, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(new RoleNewbieGift { GameUlongUID = gameUlongUID, GiftID = grant.GiftID, FetchTime = now });
            var beforeCoinNums = new Dictionary<int, long>();
            foreach (var (coinID, amount) in grant.Coins)
            {
                beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
                await WalletRepository.CreditAsync(database, gameUlongUID, coinID, amount);
            }

            foreach (var pokemonId in grant.PokemonIds)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, new RolePokemonInfo(
                    pokemonId,
                    now,
                    NewbieGiftManager.NewbieGiftReason,
                    RolePokemonInfo.HasNewTag)));
            }

            var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
            await transaction.CommitAsync();
            return new NewbieGiftCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, wallet, beforeCoinNums);
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return new NewbieGiftCommit(PB_ENUM_RES_ERROR_CODE.PbErrNewbieGiftAlreadyFetched, null, new Dictionary<int, long>());
        }
    }
}
