using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Activity;

public sealed record MegaEvolutionOutcome(
    PB_ENUM_RES_ERROR_CODE Result,
    GamePacket[] BeforeRsp,
    pbCOMDT_MEGAEVOLUTION_ACTIVITY_INFO? DataInfo = null,
    int[]? TrainResults = null)
{
    public static MegaEvolutionOutcome Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed class MegaEvolutionManager(
    GameData gameData,
    ActivitySchedule schedule,
    ActivityManager activityManager,
    ActivityRewardNotices rewardNotices,
    MegaEvolutionRepository repository,
    IOptionsMonitor<MegaEvolutionOptions> options,
    TimeProvider clock,
    ILogger<MegaEvolutionManager> logger)
{
    public const int MaxComplete = 100;

    private const int AllRewards = 0;
    private const int FirstStage = 1;
    private const int MaxTrainNum = 10;
    private const int MinBuyNum = 1;
    private const int NoOverflowComplete = 0;
    private const int NoLoop = 0;
    private const int DaysPerWeek = 7;
    private const int RewardLocked = 0;
    private const int RewardCanReceive = 1;
    private const int RewardReceived = 2;
    private const int Consum = (int)PB_ENUM_RES_MEGA_EVOLUTION_ITEM.PbResMegaEvolutionItemConsum;
    private const int ConsumNum = (int)PB_ENUM_RES_MEGA_EVOLUTION_ITEM.PbResMegaEvolutionItemConsumNum;
    private const int FixtaskNum = (int)PB_ENUM_RES_MEGA_EVOLUTION_ITEM.PbResMegaEvolutionItemFixtaskNum;
    private const int WeektaskNum = (int)PB_ENUM_RES_MEGA_EVOLUTION_ITEM.PbResMegaEvolutionItemWeektaskNum;
    private const int DailytaskNum = (int)PB_ENUM_RES_MEGA_EVOLUTION_ITEM.PbResMegaEvolutionItemDailytaskNum;
    private const int SuppertrainPrice = (int)PB_ENUM_RES_MEGA_EVOLUTION_ITEM.PbResMegaEvolutionItemSuppertrainPrice;
    private const int MaxRewardStage = (int)PB_ENUM_RES_MEGA_EVOLUTION_ITEM.PbResMegaEvolutionItemMaxRewardStage;
    private const int FixTask = (int)PB_ENUM_RES_MEGA_EVOLUTION_TASK_TYPE.PbResMegaEvolutionTaskTypeFix;
    private const int DailyTask = (int)PB_ENUM_RES_MEGA_EVOLUTION_TASK_TYPE.PbResMegaEvolutionTaskTypeDaily;
    private const int WeekTask = (int)PB_ENUM_RES_MEGA_EVOLUTION_TASK_TYPE.PbResMegaEvolutionTaskTypeWeek;

    public IEnumerable<int> FindOpenActivityIds() =>
        schedule.FindActive(clock.GetUtcNow())
            .Select(activity => activity.Row.ActivityID)
            .Where(HasConfig);

    public async Task<GamePacket[]> BuildLoginNtfsAsync(ulong uid)
    {
        var packets = new List<GamePacket>();
        foreach (var activityID in FindOpenActivityIds())
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMegaEvolutionActivityDataNtf, new pbSCMegaEvolutionActivityDataNtf
            {
                ActivityID = activityID,
                DataInfo = BuildDataInfo(activityID, await LoadAsync(uid, activityID)),
            }));
        }

        return [.. packets];
    }

    public async Task<MegaEvolutionOutcome> BuildDataAsync(ulong uid, int activityID)
    {
        if (!IsOpen(activityID))
        {
            return Refuse("data", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionActivityNotMatch);
        }

        return new MegaEvolutionOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, [], BuildDataInfo(activityID, await LoadAsync(uid, activityID)));
    }

    public async Task<MegaEvolutionOutcome> TrainAsync(LMNRoleInfo role, int activityID, int trainNum)
    {
        var uid = role.GameUlongUID;
        if (!IsOpen(activityID))
        {
            return Refuse("train", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionActivityNotMatch);
        }

        if (trainNum < 1 || trainNum > MaxTrainNum)
        {
            return Refuse("train", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }

        var state = await LoadAsync(uid, activityID);
        if (state.Complete >= MaxComplete)
        {
            return Refuse("train", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionCompleteMax);
        }

        var consumId = Other(activityID, Consum);
        var cost = (long)Other(activityID, ConsumNum) * trainNum;
        var bagCost = BuildCost(consumId, cost);
        var settings = options.CurrentValue;
        int[] trainResults = [.. Enumerable.Range(0, trainNum).Select(_ => Random.Shared.Next(settings.TrainMinComplete, settings.TrainMaxComplete + 1))];
        var completeAfter = Math.Min(MaxComplete, state.Complete + trainResults.Sum());
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var commit = await repository.CommitTrainAsync(uid, activityID, state.Complete, completeAfter, bagCost, now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return Refuse("train", uid, activityID, commit.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed ? commit.Result : PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionMoneyNotEnough);
        }

        logger.LogInformation(
            "Mega Evolution activity {ActivityID} trained {TrainNum} times by uid {Uid}: complete {Before} -> {After} for {Cost} of {ConsumId}",
            activityID, trainNum, uid, state.Complete, completeAfter, cost, consumId);
        return await BuildChangedAsync(role, activityID, bagCost, commit, trainResults);
    }

    public async Task<MegaEvolutionOutcome> SuperTrainAsync(LMNRoleInfo role, int activityID, int buyNum)
    {
        var uid = role.GameUlongUID;
        if (!IsOpen(activityID))
        {
            return Refuse("super train", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionActivityNotMatch);
        }

        var state = await LoadAsync(uid, activityID);
        if (state.Complete >= MaxComplete)
        {
            return Refuse("super train", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionCompleteMax);
        }

        if (buyNum < MinBuyNum || buyNum > MaxComplete - state.Complete)
        {
            return Refuse("super train", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionBuyNum);
        }

        var price = (long)Other(activityID, SuppertrainPrice) * buyNum;
        var cost = new BagChange { CoinCosts = new Dictionary<int, long> { [CoinID.Coupons] = price } };
        var commit = await repository.CommitTrainAsync(uid, activityID, state.Complete, state.Complete + buyNum, cost, clock.GetUtcNow().ToUnixTimeSeconds());
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return Refuse("super train", uid, activityID, commit.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed ? commit.Result : PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionMoneyNotEnough);
        }

        logger.LogInformation(
            "Mega Evolution activity {ActivityID} super trained {BuyNum}% by uid {Uid} for {Price} gems",
            activityID, buyNum, uid, price);
        return await BuildChangedAsync(role, activityID, cost, commit, null);
    }

    public async Task<MegaEvolutionOutcome> FinishTaskAsync(LMNRoleInfo role, int activityID, int taskType, int taskID)
    {
        var uid = role.GameUlongUID;
        if (!IsOpen(activityID))
        {
            return Refuse("task finish", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionActivityNotMatch);
        }

        var state = await LoadAsync(uid, activityID);
        if (!state.Tasks.Contains(new MegaEvolutionTask(taskType, taskID))
            || gameData.MegaEvolutionTaskPool.Rows.FirstOrDefault(pool => pool.ActivityID == activityID && pool.TaskType == taskType && pool.TaskID == taskID) is not { } poolTask
            || activityManager.PlanTaskFinish(taskID) is not { } finish)
        {
            return Refuse("task finish", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionTaskNotExist);
        }

        var taskInfo = (await activityManager.BuildTaskInfoListAsync(role, [taskID])).FirstOrDefault();
        if (taskInfo is null || taskInfo.Status != (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved)
        {
            return Refuse("task finish", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionNotBuyTask);
        }

        var cost = new BagChange { CoinCosts = new Dictionary<int, long> { [CoinID.Coupons] = poolTask.CompleteCoupons } };
        var commit = await repository.CommitTaskFinishAsync(uid, finish, cost);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return Refuse("task finish", uid, activityID, commit.Result == PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed ? PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionPayFailed : PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionMoneyNotEnough);
        }

        logger.LogInformation(
            "Mega Evolution activity {ActivityID} task {TaskID} finished by uid {Uid} for {Price} gems",
            activityID, taskID, uid, poolTask.CompleteCoupons);
        return new MegaEvolutionOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
        [
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, commit.Wallet!.BuildAcntCouponsRsp()),
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, new pbSCUpdateTaskInfoNtf
            {
                TaskInfoList = { await activityManager.BuildTaskInfoListAsync(role, [taskID]) },
            }),
        ]);
    }

    public async Task<MegaEvolutionOutcome> ClaimRewardAsync(LMNRoleInfo role, int activityID, int rewardID)
    {
        var uid = role.GameUlongUID;
        if (!IsOpen(activityID))
        {
            return Refuse("reward", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionActivityNotMatch);
        }

        var state = await LoadAsync(uid, activityID);
        var rewards = FindRewards(activityID);
        pbMegaEvolutionReward[] claimed;
        if (rewardID == AllRewards)
        {
            claimed = [.. rewards.Where(reward => RewardStatusOf(reward, state) == RewardCanReceive)];
            if (claimed.Length == 0)
            {
                return Refuse("reward", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionBatchRewardFailed);
            }
        }
        else if (rewards.FirstOrDefault(reward => reward.AwardKey == rewardID) is not { } reward)
        {
            return Refuse("reward", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        }
        else
        {
            var status = RewardStatusOf(reward, state);
            if (status != RewardCanReceive)
            {
                return Refuse("reward", uid, activityID, status == RewardReceived
                    ? PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionRewardHasReceived
                    : PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionNotEnoughComplete);
            }

            claimed = [reward];
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var grants = claimed.Select(reward => activityManager.PlanDropGrant(role, reward.AwardDropID, now)).ToArray();
        if (grants.Any(grant => grant is null))
        {
            return Refuse("reward", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var grant = ActivityDropRewards.Merge([.. grants.OfType<ActivityTaskGrant>()]);
        var reason = PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonMegaevolutionReward;
        var change = ActivityDropRewards.ToBagChange(grant, reason, now, new BagChange());
        var commit = await repository.CommitRewardAsync(uid, activityID, [.. claimed.Select(reward => reward.AwardKey)], change, now);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            return Refuse("reward", uid, activityID, commit.Result);
        }

        ActivityManager.GrantToRole(role, grant, now, reason);
        logger.LogInformation(
            "Mega Evolution activity {ActivityID} rewards [{AwardKeys}] claimed by uid {Uid}",
            activityID, string.Join(", ", claimed.Select(reward => reward.AwardKey)), uid);
        return new MegaEvolutionOutcome(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            [
                .. await rewardNotices.BuildAsync(role, ActivityDropRewards.ToRewardGroup(grant, change, commit)),
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
                {
                    ResultInfo = new pbResultInfo(),
                    AwardReason = (int)reason,
                    ResourcesList = { grant.Awards },
                }),
            ],
            BuildDataInfo(activityID, await LoadAsync(uid, activityID)));
    }

    public async Task<MegaEvolutionOutcome> EnterNextStageAsync(ulong uid, int activityID)
    {
        if (!IsOpen(activityID))
        {
            return Refuse("next stage", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionActivityNotMatch);
        }

        var state = await LoadAsync(uid, activityID);
        if (state.Complete < MaxComplete)
        {
            return Refuse("next stage", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionNotEnoughNextStage);
        }

        if (FindRewards(activityID).Any(reward => reward.StateIndex == state.CurStage && !state.RewardedAwardKeys.Contains(reward.AwardKey)))
        {
            return Refuse("next stage", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionRewardNotAllGet);
        }

        if (state.CurStage >= MaxStageOf(activityID))
        {
            return Refuse("next stage", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionMaxStage);
        }

        if (!await repository.EnterNextStageAsync(uid, activityID, state.CurStage, state.Complete))
        {
            return Refuse("next stage", uid, activityID, PB_ENUM_RES_ERROR_CODE.PbErrMegaEvolutionEnterNextStageFail);
        }

        logger.LogInformation("Mega Evolution activity {ActivityID} entered stage {Stage} for uid {Uid}", activityID, state.CurStage + 1, uid);
        return new MegaEvolutionOutcome(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, [], BuildDataInfo(activityID, await LoadAsync(uid, activityID)));
    }

    private async Task<MegaEvolutionOutcome> BuildChangedAsync(LMNRoleInfo role, int activityID, BagChange cost, ActivityRewardCommit commit, int[]? trainResults)
    {
        var packets = new List<GamePacket>();
        if (commit.ChangedItems.Count > 0)
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems)));
        }

        foreach (var (coinID, before) in commit.SpecialCoinsBefore)
        {
            packets.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, before, commit.Wallet!.GetCoinVal(coinID))));
        }

        if (cost.CoinCosts.ContainsKey(CoinID.Coupons))
        {
            packets.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, commit.Wallet!.BuildAcntCouponsRsp()));
        }

        return new MegaEvolutionOutcome(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            [.. packets],
            BuildDataInfo(activityID, await LoadAsync(role.GameUlongUID, activityID)),
            trainResults);
    }

    private Task<MegaEvolutionState> LoadAsync(ulong uid, int activityID) =>
        repository.RefreshAsync(uid, activityID, state => PlanTaskRefresh(activityID, state));

    private MegaEvolutionTaskRefresh? PlanTaskRefresh(int activityID, MegaEvolutionState state)
    {
        var now = clock.GetUtcNow();
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + DaysPerWeek - (int)DayOfWeek.Monday) % DaysPerWeek));
        var refreshed = new List<int>();
        var tasks = new List<MegaEvolutionTask>();
        if (!state.Tasks.Any(task => task.TaskType == FixTask))
        {
            refreshed.Add(FixTask);
            tasks.AddRange(DrawTasks(activityID, FixTask, Other(activityID, FixtaskNum), ordered: true));
        }

        if (state.LastDailyUpdateTime < today.ToUnixTimeSeconds())
        {
            refreshed.Add(DailyTask);
            tasks.AddRange(DrawTasks(activityID, DailyTask, Other(activityID, DailytaskNum), ordered: false));
        }

        if (state.LastWeekUpdateTime < weekStart.ToUnixTimeSeconds())
        {
            refreshed.Add(WeekTask);
            tasks.AddRange(DrawTasks(activityID, WeekTask, Other(activityID, WeektaskNum), ordered: false));
        }

        return refreshed.Count == 0
            ? null
            : new MegaEvolutionTaskRefresh(
                Math.Max(state.LastDailyUpdateTime, today.ToUnixTimeSeconds()),
                Math.Max(state.LastWeekUpdateTime, weekStart.ToUnixTimeSeconds()),
                refreshed,
                tasks);
    }

    private IEnumerable<MegaEvolutionTask> DrawTasks(int activityID, int taskType, int count, bool ordered)
    {
        var pool = gameData.MegaEvolutionTaskPool.Rows
            .Where(task => task.ActivityID == activityID && task.TaskType == taskType)
            .OrderBy(task => task.Index)
            .ToArray();
        var drawn = ordered ? pool : pool.OrderBy(_ => Random.Shared.Next()).ToArray();
        return drawn.Take(count).Select(task => new MegaEvolutionTask(taskType, task.TaskID));
    }

    private pbCOMDT_MEGAEVOLUTION_ACTIVITY_INFO BuildDataInfo(int activityID, MegaEvolutionState state) => new()
    {
        Complete = state.Complete,
        RewardInfo = new pbCOMDT_MEGAEVOLUTION_ACTIVITY_REWARD_INFO
        {
            BaseRewardStateList =
            {
                FindRewards(activityID).Select(reward => new pbCOMDT_MEGAEVOLUTION_REWARD_INFO
                {
                    AwardKey = reward.AwardKey,
                    RewardStatus = RewardStatusOf(reward, state),
                }),
            },
        },
        TaskInfo = new pbCOMDT_MEGAEVOLUTION_ACTIVITY_TASK_INFO
        {
            FixTaskList = { TaskIdsOf(state, FixTask) },
            WeekRefreshTaskList = { TaskIdsOf(state, WeekTask) },
            DailyRefreshTaskList = { TaskIdsOf(state, DailyTask) },
            LastDailyUpdateTime = state.LastDailyUpdateTime,
            LastWeekUpdateTime = state.LastWeekUpdateTime,
        },
        CurStage = state.CurStage,
        MaxStage = MaxStageOf(activityID),
        OverflowComplete = NoOverflowComplete,
        LoopNum = NoLoop,
    };

    private static IEnumerable<int> TaskIdsOf(MegaEvolutionState state, int taskType) =>
        state.Tasks.Where(task => task.TaskType == taskType).Select(task => task.TaskID);

    private static int RewardStatusOf(pbMegaEvolutionReward reward, MegaEvolutionState state) =>
        state.RewardedAwardKeys.Contains(reward.AwardKey) ? RewardReceived
        : reward.StateIndex < state.CurStage || (reward.StateIndex == state.CurStage && state.Complete >= reward.TargetComplete) ? RewardCanReceive
        : RewardLocked;

    private pbMegaEvolutionReward[] FindRewards(int activityID) =>
        [.. gameData.MegaEvolutionReward.Rows.Where(reward => reward.ActivityID == activityID).OrderBy(reward => reward.AwardKey)];

    private int MaxStageOf(int activityID) => Math.Max(FirstStage, Other(activityID, MaxRewardStage));

    private int Other(int activityID, int otherType) =>
        gameData.MegaEvolutionOther.Find(pbMegaEvolutionOther.Key(activityID, otherType))?.OtherVal ?? 0;

    private bool HasConfig(int activityID) => gameData.MegaEvolutionOther.Contains(pbMegaEvolutionOther.Key(activityID, Consum));

    private bool IsOpen(int activityID) =>
        HasConfig(activityID) && schedule.FindActive(clock.GetUtcNow()).Any(activity => activity.Row.ActivityID == activityID);

    private static BagChange BuildCost(int consumId, long cost) => CoinID.IsSpecialCoin(consumId)
        ? new BagChange { CoinCosts = new Dictionary<int, long> { [consumId] = cost } }
        : new BagChange { Costs = [new BagCost(RoleItemType.OutSideItem, consumId, (int)cost)] };

    private MegaEvolutionOutcome Refuse(string action, ulong uid, int activityID, PB_ENUM_RES_ERROR_CODE result)
    {
        logger.LogInformation("Mega Evolution {Action} on activity {ActivityID} refused for uid {Uid}: {Result}", action, activityID, uid, result);
        return MegaEvolutionOutcome.Refused(result);
    }
}
