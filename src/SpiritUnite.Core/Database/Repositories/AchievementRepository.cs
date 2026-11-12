using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Game.Achievement;
using SpiritUnite.Game.Bag;
using SpiritUnite.Packets.Messages;
using SpiritUnite.Game.Wallet;

namespace SpiritUnite.Database.Repositories;

public sealed class AchievementRepository(DataOptions options)
{
    public async Task<IReadOnlyDictionary<int, long>> FindRewardTimesAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return await database.GetTable<RoleAchievement>()
            .Where(achievement => achievement.GameUlongUID == gameUlongUID)
            .ToDictionaryAsync(achievement => achievement.AchievementID, achievement => achievement.RewardTime);
    }

    public async Task<RoleWallet?> CommitRewardAsync(ulong gameUlongUID, AchievementReward reward)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(new RoleAchievement
            {
                GameUlongUID = gameUlongUID,
                AchievementID = reward.AchievementID,
                RewardTime = reward.RewardTime,
            });
            foreach (var item in reward.Items)
            {
                await database.InsertAsync(RoleRepository.ToEntity(gameUlongUID, item));
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return null;
        }

        foreach (var (coinID, amount) in reward.Coins)
        {
            await WalletRepository.CreditAsync(database, gameUlongUID, coinID, amount);
        }

        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return wallet;
    }

    public async Task<IReadOnlySet<int>> FindScoreRewardScoresAsync(ulong gameUlongUID)
    {
        await using var database = new DataConnection(options);
        return (await database.GetTable<RoleAchievementScoreReward>()
            .Where(reward => reward.GameUlongUID == gameUlongUID)
            .Select(reward => reward.TotalScore)
            .ToArrayAsync()).ToHashSet();
    }

    public async Task<BagCommit> CommitScoreRewardAsync(ulong gameUlongUID, int totalScore, BagChange change, long rewardTime)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            await database.InsertAsync(new RoleAchievementScoreReward
            {
                GameUlongUID = gameUlongUID,
                TotalScore = totalScore,
                RewardTime = rewardTime,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        }

        var beforeCoinNums = new Dictionary<int, long>();
        foreach (var coinID in change.CoinGrants.Keys)
        {
            beforeCoinNums[coinID] = await WalletRepository.ReadCoinValAsync(database, gameUlongUID, coinID);
        }

        var applied = await BagRepository.ApplyAsync(database, gameUlongUID, change, rewardTime);
        if (applied != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return BagCommit.Refused(applied);
        }

        var changedItems = await BagRepository.ReadItemsAsync(database, gameUlongUID, change.TouchedItems);
        var wallet = await WalletRepository.ReadAsync(database, gameUlongUID);
        await transaction.CommitAsync();
        return new BagCommit(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, changedItems, wallet) { BeforeCoinNums = beforeCoinNums };
    }
}
