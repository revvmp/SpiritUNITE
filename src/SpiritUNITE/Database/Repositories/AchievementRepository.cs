using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Achievement;
using SpiritUNITE.Game.Wallet;

namespace SpiritUNITE.Database.Repositories;

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
}
