using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUNITE.Database.Models;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Database.Repositories;

public sealed record UniteChallengeState(int IsClickStart, long ClickStartTime, int RoundCnt, int[] BattleStatusList, int[] RewardStatusList);

public sealed class RallyChallengeRepository(DataOptions options)
{
    private const int Started = 1;
    private const int OneRow = 1;
    private const int CanReceive = (int)PB_ENUM_ENM_UNITE_CHALLENGE_REWARD_STATUS.PbEnmUniteChallengeRewardStatusCanReceive;
    private const int Received = (int)PB_ENUM_ENM_UNITE_CHALLENGE_REWARD_STATUS.PbEnmUniteChallengeRewardStatusReceived;

    public async Task<UniteChallengeState> FindAsync(ulong gameUlongUID, int periodId)
    {
        await using var database = new DataConnection(options);
        return await ReadAsync(database, gameUlongUID, periodId);
    }

    public async Task<bool> StartAsync(ulong gameUlongUID, int periodId, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        await EnsureRowAsync(database, gameUlongUID, periodId);
        var started = await database.GetTable<RoleUniteChallenge>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodId == periodId && row.IsClickStart != Started)
            .Set(row => row.IsClickStart, Started)
            .Set(row => row.ClickStartTime, now)
            .UpdateAsync() == OneRow;
        await transaction.CommitAsync();
        return started;
    }

    public async Task<bool> ResetAsync(ulong gameUlongUID, int periodId)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var cleared = await database.GetTable<RoleUniteChallengeBattle>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodId == periodId)
            .DeleteAsync();
        if (cleared == 0)
        {
            await transaction.RollbackAsync();
            return false;
        }

        await database.GetTable<RoleUniteChallenge>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodId == periodId)
            .Set(row => row.RoundCnt, row => row.RoundCnt + 1)
            .UpdateAsync();
        await transaction.CommitAsync();
        return true;
    }

    public async Task<UniteChallengeState?> RecordBattleAsync(ulong gameUlongUID, int periodId, int battleStatus, int maxBattleCount, int rewardCount)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var state = await ReadAsync(database, gameUlongUID, periodId);
        if (state.IsClickStart != Started || state.BattleStatusList.Length >= maxBattleCount)
        {
            await transaction.RollbackAsync();
            return null;
        }

        try
        {
            await database.InsertAsync(new RoleUniteChallengeBattle
            {
                GameUlongUID = gameUlongUID,
                PeriodId = periodId,
                BattleIndex = state.BattleStatusList.Length + 1,
                BattleStatus = battleStatus,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return null;
        }

        int[] battles = [.. state.BattleStatusList, battleStatus];
        var roundWins = Math.Min(battles.Count(status => status == (int)PB_ENUM_ENM_UNITE_CHALLENGE_BATTLE_STATUS.PbEnmUniteChallengeBattleStatusWin), rewardCount);
        for (var rewardIndex = state.RewardStatusList.Length + 1; rewardIndex <= roundWins; rewardIndex++)
        {
            await database.InsertAsync(new RoleUniteChallengeReward
            {
                GameUlongUID = gameUlongUID,
                PeriodId = periodId,
                RewardIndex = rewardIndex,
                RewardStatus = CanReceive,
            });
        }

        var recorded = await ReadAsync(database, gameUlongUID, periodId);
        await transaction.CommitAsync();
        return recorded;
    }

    public async Task<ActivityRewardCommit> CommitRewardAsync(ulong gameUlongUID, int periodId, int rewardIndex, BagChange change, long now)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        var claimed = await database.GetTable<RoleUniteChallengeReward>()
            .Where(row => row.GameUlongUID == gameUlongUID
                && row.PeriodId == periodId
                && row.RewardIndex == rewardIndex
                && row.RewardStatus == CanReceive)
            .Set(row => row.RewardStatus, Received)
            .Set(row => row.RewardTime, now)
            .UpdateAsync() == OneRow;
        if (!claimed)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrUniteChallengeRewardReceived);
        }

        var commit = await ActivityRepository.ApplyRewardChangeAsync(database, gameUlongUID, change, now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.RollbackAsync();
            return commit;
        }

        await transaction.CommitAsync();
        return commit;
    }

    private static async Task<UniteChallengeState> ReadAsync(DataConnection database, ulong gameUlongUID, int periodId)
    {
        var challenge = await database.GetTable<RoleUniteChallenge>()
            .FirstOrDefaultAsync(row => row.GameUlongUID == gameUlongUID && row.PeriodId == periodId);
        var battles = await database.GetTable<RoleUniteChallengeBattle>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodId == periodId)
            .OrderBy(row => row.BattleIndex)
            .Select(row => row.BattleStatus)
            .ToArrayAsync();
        var rewards = await database.GetTable<RoleUniteChallengeReward>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.PeriodId == periodId)
            .OrderBy(row => row.RewardIndex)
            .Select(row => row.RewardStatus)
            .ToArrayAsync();
        return new UniteChallengeState(challenge?.IsClickStart ?? 0, challenge?.ClickStartTime ?? 0, challenge?.RoundCnt ?? 0, battles, rewards);
    }

    private static async Task EnsureRowAsync(DataConnection database, ulong gameUlongUID, int periodId)
    {
        try
        {
            await database.InsertAsync(new RoleUniteChallenge { GameUlongUID = gameUlongUID, PeriodId = periodId });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
        }
    }
}
