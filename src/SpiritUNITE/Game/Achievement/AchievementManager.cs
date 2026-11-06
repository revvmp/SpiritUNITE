using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Game.Badge;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.TrainerExpGift;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Achievement;

public sealed class AchievementManager
{
    private const int NoCondition = 0;
    private const int SingleRewardCount = 1;
    private const int CompletedOnce = 1;
    private const int NotCompleted = 0;
    private const int UntrackedProcess = 0;
    private const int ShowNewTag = 1;
    private const int NoNewTag = 0;
    private const long NoPeriod = 0;

    private readonly GameData gameData;
    private readonly AchievementRepository repository;
    private readonly TaskProcessRepository taskProcesses;
    private readonly RoleRepository roleRepository;
    private readonly BadgeManager badges;
    private readonly TimeProvider clock;
    private readonly ILogger<AchievementManager> logger;
    private readonly AchievementDefinition[] definitions;
    private readonly FrozenDictionary<int, AchievementDefinition> definitionsByTaskId;
    private readonly ILookup<int, pbDrop_Static> dropsById;

    public AchievementManager(
        GameData gameData,
        AchievementRepository repository,
        TaskProcessRepository taskProcesses,
        RoleRepository roleRepository,
        BadgeManager badges,
        TimeProvider clock,
        ILogger<AchievementManager> logger)
    {
        this.gameData = gameData;
        this.repository = repository;
        this.taskProcesses = taskProcesses;
        this.roleRepository = roleRepository;
        this.badges = badges;
        this.clock = clock;
        this.logger = logger;
        definitions = [.. gameData.Achievement.Rows.Select(ToDefinition).OrderBy(definition => definition.Task.TaskID)];
        definitionsByTaskId = definitions.ToFrozenDictionary(definition => definition.Task.TaskID);
        dropsById = gameData.DropStatic.Rows.OrderBy(drop => drop.SDropSeq).ToLookup(drop => drop.SDropID);
    }

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role)
    {
        var rewardTimes = await repository.FindRewardTimesAsync(role.GameUlongUID);
        var measures = await LoadMeasuresAsync(role);
        return [.. definitions.Select(definition => Evaluate(role, definition, rewardTimes, measures).ToTaskInfo())];
    }

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role, IReadOnlySet<PB_ENUM_RES_CONDITION_ID> conditionIds)
    {
        var rewardTimes = await repository.FindRewardTimesAsync(role.GameUlongUID);
        var measures = await LoadMeasuresAsync(role);
        return
        [
            .. definitions
                .Where(definition => conditionIds.Contains((PB_ENUM_RES_CONDITION_ID)definition.Condition.ID))
                .Select(definition => Evaluate(role, definition, rewardTimes, measures).ToTaskInfo()),
        ];
    }

    public async Task<int> CountCompleteTasksAsync(LMNRoleInfo role) =>
        (await BuildTaskInfoListAsync(role)).Count(task => task.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);

    public async Task<pbCOMDT_TASK_INFO[]> RecordBattleAsync(LMNRoleInfo role, BattleTaskEvent battle)
    {
        var increments = definitions
            .Select(definition => BattleTasks.MeasureBattle(definition.Condition, battle) is { } delta
                ? new TaskProcessIncrement(definition.Task.TaskID, NoPeriod, delta, definition.Condition.TargetProcess)
                : null)
            .OfType<TaskProcessIncrement>()
            .ToArray();
        if (increments.Length == 0)
        {
            return [];
        }

        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        if (!await taskProcesses.AddAsync(role.GameUlongUID, increments, now))
        {
            logger.LogWarning("Battle achievement progress of uid {Uid} was not stored: the role row is missing", role.GameUlongUID);
            return [];
        }

        var touchedTaskIds = increments.Select(increment => increment.TaskId).ToHashSet();
        var rewardTimes = await repository.FindRewardTimesAsync(role.GameUlongUID);
        var measures = await LoadMeasuresAsync(role);
        return
        [
            .. definitions
                .Where(definition => touchedTaskIds.Contains(definition.Task.TaskID))
                .Select(definition => Evaluate(role, definition, rewardTimes, measures).ToTaskInfo()),
        ];
    }

    public async Task<int> GetScoreAsync(ulong gameUlongUID) =>
        SumScore(await repository.FindRewardTimesAsync(gameUlongUID));

    public async Task<AchievementClaim> ClaimAsync(LMNRoleInfo role, int taskId)
    {
        var uid = role.GameUlongUID;
        if (!definitionsByTaskId.TryGetValue(taskId, out var definition))
        {
            logger.LogInformation("Achievement task {TaskId} claim refused for uid {Uid}: not an achievement", taskId, uid);
            return AchievementClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist);
        }

        var rewardTimes = await repository.FindRewardTimesAsync(uid);
        var state = Evaluate(role, definition, rewardTimes, await LoadMeasuresAsync(role));
        if (state.Status != PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete)
        {
            var result = state.Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
                ? PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked
                : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
            logger.LogInformation("Achievement task {TaskId} claim refused for uid {Uid}: {Result}", taskId, uid, result);
            return AchievementClaim.Refused(result);
        }

        var rewardTime = clock.GetUtcNow().ToUnixTimeSeconds();
        var reward = BuildReward(role, definition, rewardTime);
        if (reward is null)
        {
            logger.LogInformation(
                "Achievement task {TaskId} claim refused for uid {Uid}: drop {SDropID} has a reward with no ownership path",
                taskId, uid, definition.Task.SDropID);
            return AchievementClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
        }

        var wallet = await repository.CommitRewardAsync(uid, reward);
        if (wallet is null)
        {
            logger.LogInformation("Achievement task {TaskId} claim refused for uid {Uid}: already claimed or owned", taskId, uid);
            return AchievementClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
        }

        foreach (var item in reward.Items)
        {
            role.GrantItem(item.ItemType, item.ItemId, item.GainTime, item.TimeLimited, item.NewTag);
        }

        var claimedRewardTimes = new Dictionary<int, long>(rewardTimes) { [definition.Row.AchievementID] = rewardTime };
        logger.LogInformation(
            "Achievement {AchievementID} (task {TaskId}) claimed by uid {Uid}: {CoinCount} coin and {ItemCount} item rewards",
            definition.Row.AchievementID, taskId, uid, reward.Coins.Count, reward.Items.Count);
        var claimed = state with { Status = PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded, RewardTime = rewardTime };
        return new AchievementClaim(
            PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            claimed.ToTaskInfo(),
            SumScore(claimedRewardTimes),
            reward,
            wallet);
    }

    private AchievementReward? BuildReward(LMNRoleInfo role, AchievementDefinition definition, long rewardTime)
    {
        var coins = new Dictionary<int, long>();
        var items = new List<RoleItemInfo>();
        foreach (var drop in dropsById[definition.Task.SDropID])
        {
            if (CoinID.WalletCoins.Contains(drop.ResID) && drop.ResNum > 0)
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
            }
            else if (drop.ResNum == SingleRewardCount && gameData.ContestantCardDecoration.Contains(drop.ResID))
            {
                if (!role.OwnsItem(RoleItemType.ContestantCardDecoration, drop.ResID))
                {
                    items.Add(new RoleItemInfo(
                        RoleItemType.ContestantCardDecoration,
                        drop.ResID,
                        rewardTime,
                        RoleItemInfo.Permanent,
                        RoleItemInfo.ShowNewTag));
                }
            }
            else
            {
                return null;
            }
        }

        return new AchievementReward(definition.Row.AchievementID, rewardTime, coins, items);
    }

    private int SumScore(IReadOnlyDictionary<int, long> rewardTimes) =>
        rewardTimes.Keys.Sum(achievementId => gameData.Achievement.Find(achievementId)?.Score ?? 0);

    private async Task<AchievementMeasures> LoadMeasuresAsync(LMNRoleInfo role)
    {
        var ownedBadges = (await badges.GetOwnedBadgesAsync(role.GameUlongUID))
            .Where(badge => badge.IsOwned)
            .Select(badge => gameData.BadgeBase.Find(badge.ItemId))
            .OfType<pbBadge_Base>()
            .ToArray();
        var seasonId = RankedMatch.FindCurrentSeason(gameData, clock.GetUtcNow()).Season.SeasonId;
        return new AchievementMeasures(
            await roleRepository.FindCreateTimeAsync(role.GameUlongUID),
            ownedBadges,
            seasonId,
            TrainerExpGiftManager.GetSeasonPoint(role),
            await taskProcesses.FindAsync(role.GameUlongUID));
    }

    private static int CountOwnedBadgeGroups(pbFinishCondition condition, AchievementMeasures measures) =>
        measures.OwnedBadges
            .Where(badge => condition.ParamA.Count == 0 || condition.ParamA.Contains(badge.BadgeQuality))
            .Select(badge => badge.BadgeGroupId)
            .Distinct()
            .Count();

    private AchievementState Evaluate(
        LMNRoleInfo role,
        AchievementDefinition definition,
        IReadOnlyDictionary<int, long> rewardTimes,
        AchievementMeasures measures)
    {
        var target = definition.Condition.TargetProcess;
        var process = Math.Min(MeasureProcess(role, definition.Condition, definition.Task.TaskID, measures), target);
        var status = rewardTimes.TryGetValue(definition.Row.AchievementID, out var rewardTime)
            ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
            : process >= target
                ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete
                : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved;
        return new AchievementState(
            definition.Task.TaskID,
            process,
            status,
            measures.RoleCreateTime,
            rewardTime,
            BuildProcessExtraInfo(definition.Condition, measures));
    }

    private static pbCOMDT_TASK_PROCESS_EXTRA_INFO[] BuildProcessExtraInfo(pbFinishCondition condition, AchievementMeasures measures) =>
        condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdAcntSeasonPointReach
            ?
            [
                new pbCOMDT_TASK_PROCESS_EXTRA_INFO
                {
                    ExtraType = (int)PB_ENUM_ENM_TASK_PROCESS_EXTRA_TYPE.PbEnmTaskProcessExtraTypeAcntPointSeason,
                    ExtraDetail = new pbCOMDT_PROCESS_EXTRA_INFO_UNION
                    {
                        AcntPointSeason = new pbCOMDT_PROCESS_ACNT_POINT_SEASON_INFO
                        {
                            SeasonId = measures.RankedSeasonId,
                            SeasonPoint = measures.SeasonPoint,
                        },
                    },
                    CondID = condition.ID,
                },
            ]
            : [];

    private int MeasureProcess(LMNRoleInfo role, pbFinishCondition condition, int taskId, AchievementMeasures measures) =>
        (PB_ENUM_RES_CONDITION_ID)condition.ID switch
        {
            PB_ENUM_RES_CONDITION_ID.PbResConditionTypeTrainerLevelReach => RoleProfile.GetLevel(gameData, role),
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdOwnResPokemon => role.GetOwnedPokemonIds().Length,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdOwnResAvatarAny => role.GetOwnedItems(RoleItemType.AvatarItem).Length,
            PB_ENUM_RES_CONDITION_ID.PbResConditionIdGetDiffGroupBadge => CountOwnedBadgeGroups(condition, measures),
            _ when BattleTasks.IsBattleCondition(condition) =>
                BattleTasks.FindStoredProcess(measures.StoredProcesses, taskId, NoPeriod),
            _ => UntrackedProcess,
        };

    private AchievementDefinition ToDefinition(pbAchievement row)
    {
        var task = gameData.TaskBase.Find(row.AchievementTaskID)
            ?? throw new InvalidDataException(
                $"Achievement {row.AchievementID} names task {row.AchievementTaskID}, which pbTask_Base does not have");
        var condition = task.FinishCondition.FirstOrDefault(condition => condition.ID != NoCondition)
            ?? throw new InvalidDataException($"Achievement task {task.TaskID} has no finish condition");
        return condition.TargetProcess > 0
            ? new AchievementDefinition(row, task, condition)
            : throw new InvalidDataException($"Achievement task {task.TaskID} has target {condition.TargetProcess}");
    }

    private sealed record AchievementDefinition(pbAchievement Row, pbTask_Base Task, pbFinishCondition Condition);

    private sealed record AchievementMeasures(
        long RoleCreateTime,
        IReadOnlyList<pbBadge_Base> OwnedBadges,
        int RankedSeasonId,
        int SeasonPoint,
        IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess> StoredProcesses);

    private sealed record AchievementState(
        int TaskId,
        int Process,
        PB_ENUM_ENM_TASKSTATUS Status,
        long RecvTime,
        long RewardTime,
        pbCOMDT_TASK_PROCESS_EXTRA_INFO[] ProcessExtraInfo)
    {
        public pbCOMDT_TASK_INFO ToTaskInfo() => new()
        {
            TaskId = TaskId,
            TaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemAchievement,
            TaskProcess = { Process },
            Status = (int)Status,
            RecvTime = RecvTime,
            RewardTime = RewardTime,
            TaskProcessExtraInfoList = { ProcessExtraInfo },
            CompleteTime = RewardTime,
            CompleteNum = Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved ? NotCompleted : CompletedOnce,
            NewTag = Status == PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved ? ShowNewTag : NoNewTag,
        };
    }
}

public static class AchievementScoreReward
{
    private static readonly int[] RewardScores = [100, 200, 300];

    public static pbCSDT_ACHIEVEMENT_SCORE_REWARD_DETAIL_LIST BuildDetailList() => new()
    {
        AchievementInfoList = { RewardScores.Select(score => new pbCSDT_ACHIEVEMENT_SCORE_REWARD_DETAIL { Score = score }) },
    };

    public static PB_ENUM_RES_ERROR_CODE RefuseClaim(int rewardScore, int score) =>
        RewardScores.Contains(rewardScore) && rewardScore <= score
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
}

public sealed record AchievementReward(
    int AchievementID,
    long RewardTime,
    IReadOnlyDictionary<int, long> Coins,
    IReadOnlyList<RoleItemInfo> Items)
{
    public pbSCResourcesAwardNotify BuildResourcesAwardNotify()
    {
        var awards = Coins
            .Select(coin => new pbCOMDT_ONE_RESOURCE_AWARD_INFO
            {
                ResID = coin.Key,
                ResCnt = (int)Math.Min(coin.Value, int.MaxValue),
                ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin,
            })
            .Concat(Items.Select(item => item.ToResourceAwardInfo()));
        return new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTaskaward,
            ResourcesList =
            {
                awards.Select(award => new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
                {
                    FinallyAwardInfo = award,
                    OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
                }),
            },
        };
    }
}

public sealed record AchievementClaim(
    PB_ENUM_RES_ERROR_CODE Result,
    pbCOMDT_TASK_INFO? TaskInfo,
    int Score,
    AchievementReward? Reward,
    RoleWallet? Wallet)
{
    public static AchievementClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, null, 0, null, null);
}
