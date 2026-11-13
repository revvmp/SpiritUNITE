using LinqToDB;
using LinqToDB.Async;
using LinqToDB.Data;
using MySqlConnector;
using SpiritUnite.Database.Models;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Database.Repositories;

public sealed class ShowSerialRewardRepository(DataOptions options)
{
    public async Task<IReadOnlySet<int>> FindRewardedSeqsAsync(ulong gameUlongUID, int activityID)
    {
        await using var database = new DataConnection(options);
        return (await database.GetTable<RoleShowSerialReward>()
            .Where(row => row.GameUlongUID == gameUlongUID && row.ActivityID == activityID)
            .Select(row => row.RewardSeq)
            .ToArrayAsync())
            .ToHashSet();
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlySet<int>>> FindRewardedSeqsAsync(ulong gameUlongUID, IReadOnlyCollection<int> activityIDs)
    {
        await using var database = new DataConnection(options);
        var rows = await database.GetTable<RoleShowSerialReward>()
            .Where(row => row.GameUlongUID == gameUlongUID && activityIDs.Contains(row.ActivityID))
            .Select(row => new { row.ActivityID, row.RewardSeq })
            .ToArrayAsync();
        return rows.GroupBy(row => row.ActivityID)
            .ToDictionary(group => group.Key, group => (IReadOnlySet<int>)group.Select(row => row.RewardSeq).ToHashSet());
    }

    public async Task<ActivityRewardCommit> CommitRewardAsync(ulong gameUlongUID, int activityID, int rewardSeq, ActivityRewardChange rewardChange, long rewardTime)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        if (!await TaskProcessRepository.LockRoleAsync(database, gameUlongUID))
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrActivityDataErr);
        }

        try
        {
            await database.InsertAsync(new RoleShowSerialReward
            {
                GameUlongUID = gameUlongUID,
                ActivityID = activityID,
                RewardSeq = rewardSeq,
                RewardTime = rewardTime,
            });
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return ActivityRewardCommit.Refused(PB_ENUM_RES_ERROR_CODE.PbErrRewardAlreadyReceived);
        }

        var commit = await ActivityRepository.ApplyRewardChangeAsync(
            database,
            gameUlongUID,
            rewardChange,
            rewardTime,
            PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonShowSerialRewardActivityReward,
            PB_ENUM_RES_ERROR_CODE.PbErrActivityDataErr);
        if (commit.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }

        return commit;
    }

    public async Task<bool> CommitUnclaimedMailAsync(ulong gameUlongUID, int activityID, IReadOnlyCollection<int> rewardSeqs, IReadOnlyList<RoleMail> mails, long mailTime)
    {
        await using var database = new DataConnection(options);
        await using var transaction = await database.BeginTransactionAsync();
        try
        {
            foreach (var rewardSeq in rewardSeqs)
            {
                await database.InsertAsync(new RoleShowSerialReward
                {
                    GameUlongUID = gameUlongUID,
                    ActivityID = activityID,
                    RewardSeq = rewardSeq,
                    RewardTime = mailTime,
                    Mailed = true,
                });
            }
        }
        catch (MySqlException exception) when (exception.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            await transaction.RollbackAsync();
            return false;
        }

        foreach (var mail in mails)
        {
            await MailRepository.InsertAsync(database, mail);
        }

        await transaction.CommitAsync();
        return true;
    }
}
