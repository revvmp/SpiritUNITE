using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Models;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Guild;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Activity;

public sealed record TenGamesBPOutcome(PB_ENUM_RES_ERROR_CODE Result, pbCOMDT_TEN_GAMESA_CTIVITY_BP_INFO Info, ActivityRewardGroup? Rewards = null);

public sealed class TenGamesBPActivityManager(
    TenGamesRepository repository,
    ActivitySchedule schedule,
    GameData gameData,
    BagManager bag,
    TimeProvider clock,
    ILogger<TenGamesBPActivityManager> logger)
{
    public const int FreeReward = 0;
    public const int SenierReward = 1;
    public const int AllRewards = 2;

    private const int NoActivityID = 0;
    private const int NotBought = 0;
    private const int RewardLocked = 0;
    private const int RewardReceived = 1;
    private const int RewardReceivable = 2;
    private const int SenierCertCoinID = CoinID.Coupons;

    public int FindSettlementActivityID(long now) =>
        schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeBattlePassCompetion, DateTimeOffset.FromUnixTimeSeconds(now))?.Row.ActivityID ?? NoActivityID;

    public async Task RecordWinAsync(ulong uid)
    {
        if (FindOpenActivity() is { } activity)
        {
            await repository.AddBPWinAsync(uid, activity.Row.ActivityID);
        }
    }

    public async Task<TenGamesBPOutcome> InfoAsync(ulong uid, int activityID)
    {
        if (FindOpenActivity(activityID) is not { } activity)
        {
            return new TenGamesBPOutcome(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, new pbCOMDT_TEN_GAMESA_CTIVITY_BP_INFO());
        }

        return new TenGamesBPOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, await BuildInfoAsync(uid, activity.Row.ActivityID));
    }

    public async Task<TenGamesBPOutcome> BuySenierRewardAsync(LMNRoleInfo role, int activityID)
    {
        var uid = role.GameUlongUID;
        if (FindOpenActivity(activityID) is not { } activity
            || gameData.TenGamesBPActivityOption.Find(activity.Row.ActivityID) is not { } option)
        {
            return new TenGamesBPOutcome(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, new pbCOMDT_TEN_GAMESA_CTIVITY_BP_INFO());
        }

        var openID = activity.Row.ActivityID;
        var cost = GuildRewardGrant.Empty with
        {
            Change = new BagChange { CoinCosts = new Dictionary<int, long> { [SenierCertCoinID] = option.SenierCertCoupons } },
        };
        var commit = await repository.CommitBPBuyAsync(uid, openID, clock.GetUtcNow().ToUnixTimeSeconds(), cost);
        if (commit.Wallet is null)
        {
            var refused = commit.Result == PB_ENUM_RES_ERROR_CODE.PbErrNotEnoughCoin
                ? PB_ENUM_RES_ERROR_CODE.PbErrTenGamesBpactivityCouponNotEnough
                : commit.Result;
            logger.LogInformation("Uid {Uid} could not unlock the senior rewards of ten games pass {ActivityID}: {Result}", uid, openID, refused);
            return new TenGamesBPOutcome(refused, await BuildInfoAsync(uid, openID));
        }

        logger.LogInformation("Uid {Uid} unlocked the senior rewards of ten games pass {ActivityID} for {Price} gems", uid, openID, option.SenierCertCoupons);
        return new TenGamesBPOutcome(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            await BuildInfoAsync(uid, openID),
            new ActivityRewardGroup(commit.Wallet, [.. cost.Change.CoinCosts.Keys], commit.SpecialCoinsBefore, commit.ChangedItems, [], []));
    }

    public async Task<TenGamesBPOutcome> GetRewardAsync(LMNRoleInfo role, int activityID, int rewardSeq, int receiveRewardType)
    {
        var uid = role.GameUlongUID;
        if (FindOpenActivity(activityID) is not { } activity)
        {
            return new TenGamesBPOutcome(PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit, new pbCOMDT_TEN_GAMESA_CTIVITY_BP_INFO());
        }

        var openID = activity.Row.ActivityID;
        var state = await LoadStateAsync(uid, openID);
        var wanted = receiveRewardType switch
        {
            AllRewards => state.Rewards
                .SelectMany(reward => new[] { (reward.Row, Type: FreeReward, Status: reward.Free), (reward.Row, Type: SenierReward, Status: reward.Senier) })
                .Where(reward => reward.Status == RewardReceivable)
                .Select(reward => (reward.Row, reward.Type))
                .ToArray(),
            FreeReward or SenierReward => state.Rewards
                .Where(reward => reward.Row.Seq == rewardSeq)
                .Where(reward => (receiveRewardType == FreeReward ? reward.Free : reward.Senier) == RewardReceivable)
                .Select(reward => (reward.Row, Type: receiveRewardType))
                .ToArray(),
            _ => [],
        };
        if (wanted.Length == 0)
        {
            return new TenGamesBPOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, state.Info);
        }

        var drops = wanted
            .SelectMany(reward => schedule.FindDrops(reward.Type == FreeReward ? reward.Row.FreeDropID : reward.Row.SenierDropID))
            .ToArray();
        var rewardTime = clock.GetUtcNow().ToUnixTimeSeconds();
        if (drops.Length == 0 || GuildRewardGrant.Plan(gameData, bag, role, drops, rewardTime) is not { } grant)
        {
            logger.LogWarning("Ten games pass {ActivityID} reward {RewardSeq} has a drop with no grant path", openID, rewardSeq);
            return new TenGamesBPOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed, state.Info);
        }

        var commit = await repository.CommitBPRewardAsync(
            uid,
            rewardTime,
            [.. wanted.Select(reward => new RoleTenGamesBPReward
            {
                GameUlongUID = uid,
                ActivityID = openID,
                RewardSeq = reward.Row.Seq,
                ReceiveRewardType = reward.Type,
                RewardTime = rewardTime,
            })],
            grant);
        if (commit.Wallet is null)
        {
            return new TenGamesBPOutcome(commit.Result, state.Info);
        }

        GuildActivityManager.GrantItemsInMemory(role, grant);
        logger.LogInformation("Uid {Uid} claimed {RewardCount} ten games pass {ActivityID} rewards", uid, wanted.Length, openID);
        return new TenGamesBPOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, await BuildInfoAsync(uid, openID), GuildRewardClaim.Granted(grant, commit).Rewards);
    }

    private ScheduledActivity? FindOpenActivity(int activityID = NoActivityID)
    {
        var open = schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeBattlePassCompetion, clock.GetUtcNow());
        return open is not null && (activityID == NoActivityID || activityID == open.Row.ActivityID) ? open : null;
    }

    private async Task<pbCOMDT_TEN_GAMESA_CTIVITY_BP_INFO> BuildInfoAsync(ulong uid, int activityID) => (await LoadStateAsync(uid, activityID)).Info;

    private async Task<TenGamesBPState> LoadStateAsync(ulong uid, int activityID)
    {
        var bp = await repository.FindBPAsync(uid, activityID);
        var winCnt = bp?.WinCnt ?? 0;
        var bought = (bp?.IsBuySenierReward ?? NotBought) != NotBought;
        var received = (await repository.GetBPRewardsAsync(uid, activityID))
            .Select(reward => (reward.RewardSeq, reward.ReceiveRewardType))
            .ToHashSet();
        var rewards = gameData.TenGamesBPActivityReward.Rows
            .Where(row => row.ActivityID == activityID)
            .OrderBy(row => row.Seq)
            .Select(row => new TenGamesBPRewardState(
                row,
                StatusOf(received.Contains((row.Seq, FreeReward)), winCnt >= row.WinCnt),
                StatusOf(received.Contains((row.Seq, SenierReward)), bought && winCnt >= row.WinCnt)))
            .ToArray();
        var info = new pbCOMDT_TEN_GAMESA_CTIVITY_BP_INFO
        {
            IsBuySenierReward = bought ? 1 : 0,
            WinCnt = winCnt,
            RewardInfo = new pbCOMDT_TEN_GAMESA_CTIVITY_BP_REWARD_LIST
            {
                RewardList =
                {
                    rewards.Select(reward => new pbCOMDT_TEN_GAMESA_CTIVITY_BP_REWARD_INFO
                    {
                        RewardSeq = reward.Row.Seq,
                        FreeRewardStatus = reward.Free,
                        SenierRewardStatus = reward.Senier,
                    }),
                },
            },
        };
        return new TenGamesBPState(info, rewards);
    }

    private static int StatusOf(bool received, bool reached) =>
        received ? RewardReceived : reached ? RewardReceivable : RewardLocked;

    private sealed record TenGamesBPRewardState(pbTenGamesBPActivityReward Row, int Free, int Senier);

    private sealed record TenGamesBPState(pbCOMDT_TEN_GAMESA_CTIVITY_BP_INFO Info, IReadOnlyList<TenGamesBPRewardState> Rewards);
}
