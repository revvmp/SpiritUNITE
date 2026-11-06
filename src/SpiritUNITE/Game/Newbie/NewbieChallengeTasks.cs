using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Pokemon;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Newbie;

public sealed record NewbieChallengeClaim(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<GamePacket> Pushes)
{
    public static NewbieChallengeClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed record NewbieChallengeUnlock(pbSCUpdateTaskInfoNtf ReceivedTasks, IReadOnlyList<GamePacket> DayTaskProgress);

public sealed record NewbieChallengeGrant(
    BagChange Change,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards);

public sealed class NewbieChallengeTasks(
    GameData gameData,
    NewbieChallengeManager challenges,
    LobbyNotices notices,
    NewbieChallengeRepository repository,
    BagManager bag,
    StarRoadManager starRoad,
    TaskUpdates taskUpdates,
    TimeProvider clock,
    ILogger<NewbieChallengeTasks> logger)
{
    private const int TaskSystem = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemNewbiechallenge;
    private const int TaskAwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTaskaward;
    private const PB_ENUM_RES_RED_DOT_ID RewardRedDotID = PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdNewbieChallengeReward;
    private const int FirstTaskDay = 1;
    private const int NoTask = 0;
    private const int NewbieParamIndex = 0;
    private const int CompletedOnce = 1;
    private const int ShowNewTag = 1;
    private const int NotStarted = 0;

    private readonly ILookup<int, pbDrop_Static> dropsById = gameData.DropStatic.Rows
        .OrderBy(drop => drop.SDropSeq)
        .ToLookup(drop => drop.SDropID);

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role) =>
        [.. (await LoadTasksAsync(role)).Select(task => task.Info)];

    public async Task<NewbieChallengeUnlock> BuildUnlockAsync(LMNRoleInfo role)
    {
        var tasks = await LoadTasksAsync(role);
        var receivedTasks = tasks.Select(task => IsDayTask(task.Task) ? task.Received : task.Info).ToArray();
        var progressedDayTasks = tasks
            .Where(task => IsDayTask(task.Task) && task.Info.TaskProcess.Any(process => process > NotStarted))
            .Select(task => task.Info)
            .ToArray();
        var allTasks = tasks.Select(task => task.Info).ToArray();
        GamePacket[] dayTaskProgress = progressedDayTasks.Length == 0
            ? []
            :
            [
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, new pbSCUpdateTaskInfoNtf { TaskInfoList = { progressedDayTasks } }),
                .. CountClaimable(allTasks) > 0 ? new[] { BuildRewardRedDot(allTasks) } : [],
            ];
        return new NewbieChallengeUnlock(new pbSCUpdateTaskInfoNtf { TaskInfoList = { receivedTasks } }, dayTaskProgress);
    }

    public async Task<NewbieChallengeClaim> ClaimAsync(LMNRoleInfo role, int taskId)
    {
        var uid = role.GameUlongUID;
        var refusal = await FindClaimRefusalAsync(role, taskId);
        if (refusal is { } refused)
        {
            logger.LogInformation("Beginner Challenge task {TaskId} claim refused for uid {Uid}: {Result}", taskId, uid, refused);
            return NewbieChallengeClaim.Refused(refused);
        }

        var task = gameData.TaskBase.Find(taskId)!;
        var rewardTime = clock.GetUtcNow().ToUnixTimeSeconds();
        var grant = PlanGrant(role, task, rewardTime);
        if (grant is null)
        {
            logger.LogWarning(
                "Beginner Challenge task {TaskId} claim refused for uid {Uid}: drop {SDropID} has a reward with no grant rule",
                taskId, uid, task.SDropID);
            return NewbieChallengeClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeRewardFail);
        }

        var commit = await repository.CommitTaskRewardAsync(uid, taskId, rewardTime, grant.Change);
        if (commit.Result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc || commit.Wallet is not { } wallet)
        {
            logger.LogInformation("Beginner Challenge task {TaskId} claim refused for uid {Uid}: {Result}", taskId, uid, commit.Result);
            return NewbieChallengeClaim.Refused(commit.Result);
        }

        foreach (var pokemon in grant.Change.PokemonGrants)
        {
            role.GrantPokemon(pokemon.PokemonId, rewardTime, TaskAwardReason);
        }

        logger.LogInformation(
            "Beginner Challenge task {TaskId} claimed by uid {Uid}: {RewardCount} rewards granted",
            taskId, uid, grant.Awards.Count);
        var tasks = await BuildTaskInfoListAsync(role);
        var pushes = new List<GamePacket>
        {
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, new pbSCUpdateTaskInfoNtf
            {
                TaskInfoList = { tasks.Where(info => info.TaskId == taskId) },
            }),
            BuildRewardRedDot(tasks),
        };
        foreach (var coinID in grant.Change.CoinGrants.Keys.Where(CoinID.IsSpecialCoin))
        {
            pushes.Add(GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidSpecialCoinChangeNtf,
                SpecialCoin.BuildSpecialCoinChangeNtf(coinID, commit.BeforeCoinNums.GetValueOrDefault(coinID), wallet.GetCoinVal(coinID))));
        }

        foreach (var pokemon in grant.Change.PokemonGrants)
        {
            pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdatePokemonNtf, new pbSCUpdatePokemonNtf
            {
                UpdateType = (int)PB_ENUM_ENM_POKEMON_UPDATETYPE.PbEnmPokemonUpdatetypeGain,
                PokemonInfo = role.BuildPokemonInfo(pokemon.PokemonId),
            }));
        }

        if (grant.Change.PokemonGrants.Count > 0)
        {
            pushes.AddRange(await starRoad.BuildPokemonGainPacketsAsync(role));
            pushes.Add(StarRoadManager.BuildAddPokemonRedDot(role));
        }
        else if (commit.BeforeCoinNums.TryGetValue(CoinID.UNIVERSAL_SHARDS_TOKEN_ID, out var licensePointsBefore))
        {
            pushes.AddRange(await starRoad.BuildSpecialCoinChangePacketsAsync(role, licensePointsBefore));
        }

        if (grant.Change.CoinGrants.Keys.Any(CoinID.WalletCoins.Contains))
        {
            pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() }));
        }

        if (commit.ChangedItems.Count > 0)
        {
            pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems)));
        }

        pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
        {
            ResultInfo = new pbResultInfo(),
            AwardReason = TaskAwardReason,
            ResourcesList = { grant.Awards },
        }));
        pushes.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)));
        return new NewbieChallengeClaim(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc, pushes);
    }

    private async Task<PB_ENUM_RES_ERROR_CODE?> FindClaimRefusalAsync(LMNRoleInfo role, int taskId)
    {
        if (FindTaskDay(taskId) is not { } taskDay || gameData.TaskBase.Find(taskId) is null)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist;
        }

        if (!challenges.IsUnlocked(role))
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeUnlock;
        }

        var unlockInfo = await challenges.BuildUnlockInfoAsync(role);
        if (unlockInfo.LeftTime <= 0)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeClosed;
        }

        if (taskDay > unlockInfo.UnlockDays)
        {
            return PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeUnlock;
        }

        var info = (await BuildTaskInfoListAsync(role)).FirstOrDefault(task => task.TaskId == taskId);
        return (PB_ENUM_ENM_TASKSTATUS?)info?.Status switch
        {
            PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete => null,
            PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded => PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeRewarded,
            _ => PB_ENUM_RES_ERROR_CODE.PbErrNewbieChallengeUnfinished,
        };
    }

    private NewbieChallengeGrant? PlanGrant(LMNRoleInfo role, pbTask_Base task, long rewardTime)
    {
        var coins = new Dictionary<int, long>();
        var grants = new List<BagGrant>();
        var pokemonIds = new List<int>();
        var awards = new List<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO>();
        foreach (var drop in dropsById[task.SDropID])
        {
            if (drop.ResNum <= 0)
            {
                return null;
            }

            if (CoinID.IsGrantableCoin(drop.ResID))
            {
                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
                awards.Add(BagManager.BuildCoinAward(drop.ResID, drop.ResNum));
            }
            else if (UniteLicense.IsUniteLicense(gameData, drop.ResID))
            {
                if (UniteLicense.PlanGrant(gameData, role, drop.ResID, drop.ResNum, pokemonIds) is not { } license)
                {
                    return null;
                }

                license.AddTo(pokemonIds, coins, awards);
            }
            else if (bag.CreateOutSideItemGrant(drop.ResID, drop.ResNum) is { } grant)
            {
                grants.Add(grant);
                awards.Add(BagManager.BuildAward(drop.ResID, drop.ResNum, PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesOutsideitem));
            }
            else
            {
                return null;
            }
        }

        return awards.Count == 0
            ? null
            : new NewbieChallengeGrant(
                new BagChange
                {
                    Grants = grants,
                    CoinGrants = coins,
                    PokemonGrants = [.. pokemonIds.Select(pokemonId => new RolePokemonInfo(pokemonId, rewardTime, TaskAwardReason, RolePokemonInfo.HasNewTag))],
                },
                awards);
    }

    private static GamePacket BuildRewardRedDot(IEnumerable<pbCOMDT_TASK_INFO> tasks) =>
        TaskRedDots.BuildCount(RewardRedDotID, CountClaimable(tasks));

    private static int CountClaimable(IEnumerable<pbCOMDT_TASK_INFO> tasks) =>
        tasks.Count(task => task.Status == (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete);

    private int GetSchemeId() =>
        notices.GetBusinessSchemeId(PB_ENUM_RES_USER_SCHEME_BUSINESS_TYPE.PbResUserSchemeBusinessTypeNewbieChallenge);

    private int? FindTaskDay(int taskId)
    {
        var schemeId = GetSchemeId();
        return gameData.NewbieChallengeTask.Rows
            .Where(day => day.SchemeId == schemeId && (day.DayTaskID == taskId || day.SubTaskID.Contains(taskId)))
            .Select(day => (int?)day.TaskDay)
            .FirstOrDefault();
    }

    private bool IsDayTask(pbTask_Base task)
    {
        var schemeId = GetSchemeId();
        return gameData.NewbieChallengeTask.Rows.Any(day => day.SchemeId == schemeId && day.DayTaskID == task.TaskID);
    }

    private async Task<ChallengeTask[]> LoadTasksAsync(LMNRoleInfo role)
    {
        if (!challenges.IsUnlocked(role))
        {
            return [];
        }

        var recvTime = await repository.ReceiveAsync(role.GameUlongUID, clock.GetUtcNow().ToUnixTimeSeconds());
        var rewardTimes = await repository.FindRewardTimesAsync(role.GameUlongUID);
        var unlockDays = (await challenges.BuildUnlockInfoAsync(role)).UnlockDays;
        var schemeId = GetSchemeId();
        return
        [
            .. Enumerable.Range(FirstTaskDay, Math.Max(0, unlockDays))
                .Select(taskDay => gameData.NewbieChallengeTask.Find(pbNewbieChallengeTask.Key(schemeId, taskDay)))
                .OfType<pbNewbieChallengeTask>()
                .SelectMany(day => day.SubTaskID.Prepend(day.DayTaskID))
                .Where(taskId => taskId != NoTask)
                .Select(taskId => gameData.TaskBase.Find(taskId))
                .OfType<pbTask_Base>()
                .Select(task => new ChallengeTask(
                    task,
                    BuildTaskInfo(role, task, recvTime, rewardTimes, countsSubTasks: true),
                    BuildTaskInfo(role, task, recvTime, rewardTimes, countsSubTasks: false))),
        ];
    }

    private pbCOMDT_TASK_INFO BuildTaskInfo(
        LMNRoleInfo role,
        pbTask_Base task,
        long recvTime,
        IReadOnlyDictionary<int, long> rewardTimes,
        bool countsSubTasks)
    {
        var condition = task.FinishCondition.FirstOrDefault();
        var target = condition?.TargetProcess ?? 0;
        var process = condition is null ? NotStarted : Math.Min(target, Evaluate(role, condition, countsSubTasks));
        var complete = target > 0 && process >= target;
        var rewarded = rewardTimes.TryGetValue(task.TaskID, out var rewardTime);
        var info = new pbCOMDT_TASK_INFO
        {
            TaskId = task.TaskID,
            TaskSystem = TaskSystem,
            TaskProcess = { process },
            Status = (int)(rewarded
                ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded
                : complete ? PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete : PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved),
            RecvTime = recvTime,
            RewardTime = rewarded ? rewardTime : 0,
            NewTag = rewarded ? 0 : ShowNewTag,
        };
        if (complete || rewarded)
        {
            info.CompleteNum = CompletedOnce;
            info.CompleteTime = recvTime;
        }

        return info;
    }

    private int Evaluate(LMNRoleInfo role, pbFinishCondition condition, bool countsSubTasks) => (PB_ENUM_RES_CONDITION_ID)condition.ID switch
    {
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdCompleteNewbie =>
            condition.ParamA.Count > NewbieParamIndex
            && LMNRoleInfo.IsNewbieInRange(condition.ParamA[NewbieParamIndex])
            && role.IsNewbieStateSet(condition.ParamA[NewbieParamIndex])
                ? CompletedOnce
                : NotStarted,
        PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinishTask when countsSubTasks => condition.ParamA
            .Select(taskId => gameData.TaskBase.Find(taskId))
            .OfType<pbTask_Base>()
            .Count(subTask => subTask.FinishCondition.FirstOrDefault() is { ID: not (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinishTask } subCondition
                && subCondition.TargetProcess > 0
                && Evaluate(role, subCondition, countsSubTasks) >= subCondition.TargetProcess),
        _ => NotStarted,
    };

    private sealed record ChallengeTask(pbTask_Base Task, pbCOMDT_TASK_INFO Info, pbCOMDT_TASK_INFO Received);
}
