using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Bag;
using SpiritUnite.Game.Lobby;
using SpiritUnite.Game.Pokemon;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.StarRoad;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Newbie;

public sealed record NewbieChallengeClaim(PB_ENUM_RES_ERROR_CODE Result, IReadOnlyList<GamePacket> Pushes)
{
    public static NewbieChallengeClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed record NewbieChallengeUnlock(pbSCUpdateTaskInfoNtf ReceivedTasks, IReadOnlyList<GamePacket> DayTaskProgress);

public sealed record NewbieChallengeGrant(
    BagChange Change,
    IReadOnlyList<pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO> Awards);

public sealed class NewbieChallengeTaskManager(
    GameData gameData,
    NewbieChallengeManager challenges,
    LobbyNotices notices,
    NewbieChallengeRepository repository,
    TaskProcessRepository taskProcesses,
    BagManager bag,
    StarRoadManager starRoad,
    TaskUpdates taskUpdates,
    TimeProvider clock,
    ILogger<NewbieChallengeTaskManager> logger)
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
    private const long NoResetPeriod = 0;

    private readonly ILookup<int, pbDrop_Static> dropsById = gameData.DropStatic.Rows
        .OrderBy(drop => drop.SDropSeq)
        .ToLookup(drop => drop.SDropID);

    public async Task<pbCOMDT_TASK_INFO[]> BuildTaskInfoListAsync(LMNRoleInfo role) =>
        [.. (await LoadTasksAsync(role)).Select(task => task.Info)];

    public async Task<pbCOMDT_TASK_INFO[]> ApplySettlementAsync(SettlementKey key, NewbieChallengeSettlementPlan plan)
    {
        if (plan.ReceiveChallenge)
        {
            await repository.ReceiveAsync(key.GameUlongUID, plan.Tasks.Now);
            await repository.ReceiveDaysAsync(key.GameUlongUID, plan.TaskDays, plan.Tasks.Now);
        }

        return await taskProcesses.ApplySettlementAsync(key, plan.Tasks);
    }

    public async Task<NewbieChallengeSettlementPlan> PrepareSettlementAsync(LMNRoleInfo role, BattleTaskEvent battle, DateTimeOffset now)
    {
        const string effect = "tasks.newbie";
        var challenge = await LoadChallengeAsync(role, now, true);
        if (challenge.IsChallengeOvered)
        {
            return new NewbieChallengeSettlementPlan(SettlementTaskPlan.Empty(effect, now.ToUnixTimeSeconds()),
                Enumerable.Range(FirstTaskDay + 1, Math.Max(0, challenge.UnlockDays - FirstTaskDay)).ToArray(), challenges.IsUnlocked(role));
        }

        var increments = challenge.Tasks
            .Select(task => task.Task.FinishCondition.FirstOrDefault() is { } condition && BattleTasks.MeasureBattle(gameData, condition, battle) is { } delta
                ? new TaskProcessIncrement(task.Task.TaskID, NoResetPeriod, delta, condition.TargetProcess)
                : null)
            .OfType<TaskProcessIncrement>()
            .ToArray();
        var touched = increments.Select(increment => increment.TaskId).ToHashSet();
        return new NewbieChallengeSettlementPlan(new SettlementTaskPlan(effect, now.ToUnixTimeSeconds(), increments,
            [.. challenge.Tasks.Where(task => touched.Contains(task.Task.TaskID)).Select(task => new SettlementTaskTemplate(
                task.Info, NoResetPeriod, [task.Task.FinishCondition[0].TargetProcess], [0], SettlementTaskCompleteTime.Received))]),
            Enumerable.Range(FirstTaskDay + 1, Math.Max(0, challenge.UnlockDays - FirstTaskDay)).ToArray(), true);
    }

    public async Task<pbCOMDT_TASK_INFO[]> RecordBattleAsync(LMNRoleInfo role, BattleTaskEvent battle)
    {
        var challenge = await LoadChallengeAsync(role);
        if (challenge.IsChallengeOvered)
        {
            logger.LogDebug("Beginner Challenge progress of uid {Uid} not counted: the challenge period is over", role.GameUlongUID);
            return [];
        }

        var increments = challenge.Tasks
            .Select(task => task.Task.FinishCondition.FirstOrDefault() is { } condition && BattleTasks.MeasureBattle(gameData, condition, battle) is { } delta
                ? new TaskProcessIncrement(task.Task.TaskID, NoResetPeriod, delta, condition.TargetProcess)
                : null)
            .OfType<TaskProcessIncrement>()
            .ToArray();
        return await AddProcessesAsync(role, increments);
    }

    public async Task<pbCOMDT_TASK_INFO[]> RecordProfilePhotoChangeAsync(LMNRoleInfo role)
    {
        var challenge = await LoadChallengeAsync(role);
        if (challenge.IsChallengeOvered)
        {
            return [];
        }

        var increments = challenge.Tasks
            .Where(task => task.Task.FinishCondition.FirstOrDefault() is { } condition && IsProfilePhotoChange(condition))
            .Select(task => new TaskProcessIncrement(task.Task.TaskID, NoResetPeriod, CompletedOnce, task.Task.FinishCondition[0].TargetProcess))
            .ToArray();
        return await AddProcessesAsync(role, increments);
    }

    private async Task<pbCOMDT_TASK_INFO[]> AddProcessesAsync(LMNRoleInfo role, TaskProcessIncrement[] increments)
    {
        if (increments.Length == 0 || !await taskProcesses.AddAsync(role.GameUlongUID, increments, clock.GetUtcNow().ToUnixTimeSeconds()))
        {
            return [];
        }

        var touchedTaskIds = increments.Select(increment => increment.TaskId).ToHashSet();
        return
        [
            .. (await LoadTasksAsync(role))
                .Where(task => touchedTaskIds.Contains(task.Task.TaskID) || CountsAnySubTask(task.Task, touchedTaskIds))
                .Select(task => task.Info),
        ];
    }

    private static bool CountsAnySubTask(pbTask_Base task, IReadOnlySet<int> subTaskIds) =>
        task.FinishCondition.FirstOrDefault() is { ID: (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdFinishTask } condition
        && condition.ParamA.Any(subTaskIds.Contains);

    private static bool IsProfilePhotoChange(pbFinishCondition condition) =>
        condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdChangeProfilePhoto;

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

    public async Task<IReadOnlyList<GamePacket>> BuildDayUnlockPushesAsync(LMNRoleInfo role)
    {
        var challenge = await LoadChallengeAsync(role);
        if (!challenge.DaysReceivedNow.Contains(challenge.UnlockDays))
        {
            return [];
        }

        var newDayTasks = challenge.Tasks.Where(task => task.TaskDay == challenge.UnlockDays).ToArray();
        var dayTasks = newDayTasks.Where(task => IsDayTask(task.Task)).Select(task => task.Info).ToArray();
        logger.LogInformation(
            "Beginner Challenge day {TaskDay} received by uid {Uid}: {TaskCount} tasks",
            challenge.UnlockDays,
            role.GameUlongUID,
            newDayTasks.Length);
        return
        [
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, new pbSCUpdateTaskInfoNtf { TaskInfoList = { dayTasks } }),
            GamePacket.Create(
                PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf,
                new pbSCUpdateTaskInfoNtf { TaskInfoList = { newDayTasks.Select(task => task.Info) } }),
        ];
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

        pushes.AddRange(wallet.BuildCoinValPushes(grant.Change.CoinGrants.Keys));

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

    private async Task<ChallengeTask[]> LoadTasksAsync(LMNRoleInfo role) => (await LoadChallengeAsync(role)).Tasks;

    private async Task<LoadedChallenge> LoadChallengeAsync(LMNRoleInfo role, DateTimeOffset? at = null, bool readOnly = false)
    {
        if (!challenges.IsUnlocked(role))
        {
            return new LoadedChallenge([], new HashSet<int>(), 0, true);
        }

        var now = (at ?? clock.GetUtcNow()).ToUnixTimeSeconds();
        var received = readOnly ? await repository.FindReceivedAsync(role.GameUlongUID) : default;
        var firstDayRecvTime = readOnly ? received.FirstDay ?? now : await repository.ReceiveAsync(role.GameUlongUID, now);
        var rewardTimes = await repository.FindRewardTimesAsync(role.GameUlongUID);
        var unlockInfo = await challenges.BuildUnlockInfoAsync(role, at);
        var unlockDays = Math.Max(0, unlockInfo.UnlockDays);
        var laterDays = Enumerable.Range(FirstTaskDay + 1, Math.Max(0, unlockDays - FirstTaskDay)).ToArray();
        var dayReceipt = readOnly
            ? new NewbieChallengeDayReceipt(received.Days, new HashSet<int>())
            : await repository.ReceiveDaysAsync(role.GameUlongUID, laterDays, now);
        var storedProcesses = await taskProcesses.FindAsync(role.GameUlongUID);
        var schemeId = GetSchemeId();
        ChallengeTask[] tasks =
        [
            .. Enumerable.Range(FirstTaskDay, unlockDays)
                .Select(taskDay => gameData.NewbieChallengeTask.Find(pbNewbieChallengeTask.Key(schemeId, taskDay)))
                .OfType<pbNewbieChallengeTask>()
                .SelectMany(day => day.SubTaskID.Prepend(day.DayTaskID)
                    .Where(taskId => taskId != NoTask)
                    .Select(taskId => (Day: day, Task: gameData.TaskBase.Find(taskId))))
                .Where(pair => pair.Task is not null)
                .Select(pair =>
                {
                    var recvTime = pair.Day.TaskDay == FirstTaskDay
                        ? firstDayRecvTime
                        : dayReceipt.RecvTimes.GetValueOrDefault(pair.Day.TaskDay, now);
                    return new ChallengeTask(
                        pair.Task!,
                        pair.Day.TaskDay,
                        BuildTaskInfo(role, pair.Task!, recvTime, rewardTimes, storedProcesses, countsSubTasks: true),
                        BuildTaskInfo(role, pair.Task!, recvTime, rewardTimes, storedProcesses, countsSubTasks: false));
                }),
        ];
        return new LoadedChallenge(tasks, dayReceipt.ReceivedNow, unlockDays, challenges.IsChallengeOvered(unlockInfo));
    }

    private pbCOMDT_TASK_INFO BuildTaskInfo(
        LMNRoleInfo role,
        pbTask_Base task,
        long recvTime,
        IReadOnlyDictionary<int, long> rewardTimes,
        IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess> storedProcesses,
        bool countsSubTasks)
    {
        var condition = task.FinishCondition.FirstOrDefault();
        var target = condition?.TargetProcess ?? 0;
        var process = condition is null
            ? NotStarted
            : Math.Min(target, MeasureProcess(role, task, condition, storedProcesses, countsSubTasks));
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

    private int MeasureProcess(
        LMNRoleInfo role,
        pbTask_Base task,
        pbFinishCondition condition,
        IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess> storedProcesses,
        bool countsSubTasks) =>
        BattleTasks.IsBattleCondition(condition) || IsProfilePhotoChange(condition)
            ? BattleTasks.FindStoredProcess(storedProcesses, task.TaskID, NoResetPeriod)
            : Evaluate(role, condition, storedProcesses, countsSubTasks);

    private int Evaluate(
        LMNRoleInfo role,
        pbFinishCondition condition,
        IReadOnlyDictionary<(int TaskId, long PeriodStart), StoredTaskProcess> storedProcesses,
        bool countsSubTasks) => (PB_ENUM_RES_CONDITION_ID)condition.ID switch
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
                    && MeasureProcess(role, subTask, subCondition, storedProcesses, countsSubTasks) >= subCondition.TargetProcess),
            _ => NotStarted,
        };

    private sealed record ChallengeTask(pbTask_Base Task, int TaskDay, pbCOMDT_TASK_INFO Info, pbCOMDT_TASK_INFO Received);

    private sealed record LoadedChallenge(ChallengeTask[] Tasks, IReadOnlySet<int> DaysReceivedNow, int UnlockDays, bool IsChallengeOvered);
}

public sealed record NewbieChallengeSettlementPlan(SettlementTaskPlan Tasks, int[] TaskDays, bool ReceiveChallenge);
