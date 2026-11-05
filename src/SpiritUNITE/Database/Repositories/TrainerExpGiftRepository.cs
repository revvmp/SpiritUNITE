using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record TrainerExpGiftCommit(
    PB_ENUM_RES_ERROR_CODE Result,
    IReadOnlyDictionary<int, long> SpecialCoinsBefore,
    RoleWallet? Wallet,
    IReadOnlyList<BagItem> ChangedItems)
{
    public static TrainerExpGiftCommit Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, new Dictionary<int, long>(), null, []);
}

public sealed class TrainerExpGiftRepository(DataOptions options)
{
    public async Task<int[]> FindClaimedRewardExpsAsync(ulong gameUlongUID, int giftType)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleTrainerExpGift>()
            .Where(gift => gift.GameUlongUID == gameUlongUID && gift.GiftType == giftType)
            .Select(gift => gift.RewardExp)
            .ToArrayAsync();
    }

    public async Task<TrainerExpGiftCommit> CommitClaimAsync(
        ulong gameUlongUID,
        int giftType,
        int rewardExp,
        BagChange change,
        IReadOnlyList<int> pokemonIds,
        long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(new RoleTrainerExpGift
            {
                GameUlongUID = gameUlongUID,
                GiftType = giftType,
                RewardExp = rewardExp,
                AwardTime = now,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return TrainerExpGiftCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTrainnerExpGiftAlreadyGet);
        }

        var specialCoinsBefore = new Dictionary<int, long>();
        foreach (var coinID in change.CoinGrants.Keys.Where(CoinID.IsSpecialCoin))
        {
            specialCoinsBefore[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        try
        {
            foreach (var pokemonId in pokemonIds)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, new RolePokemonInfo(
                    pokemonId,
                    now,
                    (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonLevelGift,
                    RolePokemonInfo.HasNewTag)));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return TrainerExpGiftCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTrainnerExpGiftNotAbleToGet);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, change, now);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return TrainerExpGiftCommit.Refused(applied);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new TrainerExpGiftCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, specialCoinsBefore, wallet, changedItems);
    }
}
