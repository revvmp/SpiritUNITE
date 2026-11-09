using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Databin;
using SpiritUnite.Game.Role;
using SpiritUnite.Game.Settings;
using SpiritUnite.Game.Tasks;
using SpiritUnite.Game.Wallet;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Game.Credit;

public sealed record CreditPointTaskClaim(PB_ENUM_RES_ERROR_CODE Result, GamePacket[] Pushes)
{
    public static CreditPointTaskClaim Refused(PB_ENUM_RES_ERROR_CODE result) => new(result, []);
}

public sealed class CreditPointTaskManager(
    CreditRepository repository,
    CreditManager credit,
    GameData gameData,
    SettingsManager settings,
    TaskUpdates taskUpdates,
    TimeProvider clock,
    ILogger<CreditPointTaskManager> logger)
{
    private const int CreditPointRewardRedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdCreditPointReward;
    private const int CreditPointRewardBadgesPerCompletion = 2;
    private const int TaskSystemCreditpoint = (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemCreditpoint;
    private const int TaskstatusRecved = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRecved;
    private const int TaskstatusComplete = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusComplete;
    private const int TaskstatusRewarded = (int)PB_ENUM_ENM_TASKSTATUS.PbEnmTaskstatusRewarded;
    private const int TaskNewTag = 1;
    private const int NotCompleted = 0;
    private const long NoTime = 0;

    private readonly CreditPointTaskDefinition[] definitions =
    [
        .. gameData.TaskBase.Rows
            .Select(task => (Task: task, Condition: task.FinishCondition.FirstOrDefault(condition =>
                condition.ID == (int)PB_ENUM_RES_CONDITION_ID.PbResConditionIdCreditpoint)))
            .Where(credit => credit.Condition is not null)
            .Select(credit => new CreditPointTaskDefinition(credit.Task.TaskID, credit.Condition!.TargetProcess, credit.Task.SDropID)),
    ];

    private readonly ILookup<int, pbDrop_Static> dropsById = gameData.DropStatic.Rows.OrderBy(drop => drop.SDropSeq).ToLookup(drop => drop.SDropID);

    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> gates = new();

    private sealed record CreditPointTaskDefinition(int TaskId, int TargetProcess, int SDropID);

    public bool IsCreditPointTask(int taskId) => definitions.Any(definition => definition.TaskId == taskId);

    public Task<pbSCUpdateTaskInfoNtf> IssueAsync(ulong uid, long recvTime) =>
        WithGateAsync(uid, async () => BuildUpdateTaskInfoNtf(await IssueTasksAsync(uid, recvTime)));

    public Task<pbSCUpdateTaskInfoNtf> ReissueDailyAsync(ulong uid, long recvTime) =>
        WithGateAsync(uid, async () =>
        {
            var tasks = await IssueTasksAsync(uid, recvTime);
            var badgeNum = tasks.Count(task => task.Status == TaskstatusComplete) * CreditPointRewardBadgesPerCompletion;
            await StoreRedDotAsync(uid, badgeNum);
            logger.LogInformation("Uid {Uid} gets the daily credit-point tasks; red dot {RedDotID} = {BadgeNum}", uid, CreditPointRewardRedDotID, badgeNum);
            return BuildUpdateTaskInfoNtf(tasks);
        });

    public Task<GamePacket[]> SyncAsync(ulong uid) =>
        WithGateAsync(uid, async () => (await SyncTasksAsync(uid)).Pushes);

    public Task<(pbCOMDT_TASK_INFO[] Tasks, GamePacket[] Pushes)> BuildTaskInfoListAsync(ulong uid) =>
        WithGateAsync(uid, async () =>
        {
            var (tasks, pushes) = await SyncTasksAsync(uid);
            pbCOMDT_TASK_INFO[] taskInfos = [.. tasks.Select(ToTaskInfo)];
            return (taskInfos, pushes);
        });

    public Task<CreditPointTaskClaim> ClaimAsync(LMNRoleInfo role, int taskId) =>
        WithGateAsync(role.GameUlongUID, async () =>
        {
            var uid = role.GameUlongUID;
            var definition = definitions.FirstOrDefault(definition => definition.TaskId == taskId);
            if (definition is null)
            {
                return CreditPointTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskNotExist);
            }

            var (tasks, _) = await SyncTasksAsync(uid);
            var task = tasks.First(task => task.TaskId == taskId);
            if (task.Status != TaskstatusComplete)
            {
                var result = task.Status == TaskstatusRewarded ? PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked : PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid;
                logger.LogInformation("Credit-point task {TaskId} claim refused for uid {Uid}: {Result}", taskId, uid, result);
                return CreditPointTaskClaim.Refused(result);
            }

            var coins = new Dictionary<int, long>();
            foreach (var drop in dropsById[definition.SDropID])
            {
                if (!CoinID.WalletCoins.Contains(drop.ResID) || drop.ResNum <= 0)
                {
                    logger.LogWarning("Credit-point task {TaskId} drop {SDropID} has resource {ResID} with no grant rule", taskId, definition.SDropID, drop.ResID);
                    return CreditPointTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed);
                }

                coins[drop.ResID] = coins.GetValueOrDefault(drop.ResID) + drop.ResNum;
            }

            var rewardTime = clock.GetUtcNow().ToUnixTimeSeconds();
            var wallet = await repository.CommitTaskRewardAsync(uid, taskId, rewardTime, coins);
            if (wallet is null)
            {
                logger.LogInformation("Credit-point task {TaskId} claim refused for uid {Uid}: already claimed", taskId, uid);
                return CreditPointTaskClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrTaskRewardTaked);
            }

            logger.LogInformation("Credit-point task {TaskId} claimed by uid {Uid}: coins {Coins}", taskId, uid, coins);
            var rewarded = task with { Status = TaskstatusRewarded, RewardTime = rewardTime };
            return new CreditPointTaskClaim(PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc,
            [
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, BuildUpdateTaskInfoNtf([rewarded])),
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, new pbSCRedDotClearNtf { RedDotID = CreditPointRewardRedDotID }),
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() }),
                .. coins.ContainsKey(CoinID.Coupons)
                    ? [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidAcntCouponsRsp, wallet.BuildAcntCouponsRsp())]
                    : Array.Empty<GamePacket>(),
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, BuildResourcesAwardNotify(coins)),
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)),
            ]);
        });

    private async Task<CreditPointTaskRecord[]> IssueTasksAsync(ulong uid, long recvTime)
    {
        var creditPoint = await credit.GetCreditPointAsync(uid);
        CreditPointTaskRecord[] tasks = [.. definitions.Select(definition => Issue(definition, creditPoint, recvTime))];
        await repository.SaveTasksAsync(uid, tasks);
        return tasks;
    }

    private async Task<(CreditPointTaskRecord[] Tasks, GamePacket[] Pushes)> SyncTasksAsync(ulong uid)
    {
        var creditPoint = await credit.GetCreditPointAsync(uid);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        var saved = (await repository.ListTasksAsync(uid)).ToDictionary(task => task.TaskId);
        var tasks = new List<CreditPointTaskRecord>();
        var changed = new List<CreditPointTaskRecord>();
        var redDotSteps = new List<int>();
        foreach (var definition in definitions)
        {
            if (!saved.TryGetValue(definition.TaskId, out var task))
            {
                var issued = Issue(definition, creditPoint, now);
                tasks.Add(issued);
                changed.Add(issued);
                continue;
            }

            var reached = creditPoint >= definition.TargetProcess;
            var synced = task.Status switch
            {
                TaskstatusComplete when !reached => task with { Status = TaskstatusRecved, TaskProcess = creditPoint },
                TaskstatusRecved when reached => task with
                {
                    Status = TaskstatusComplete,
                    TaskProcess = definition.TargetProcess,
                    CompleteNum = task.CompleteNum + 1,
                    CompleteTime = now,
                },
                TaskstatusRecved when task.TaskProcess != creditPoint => task with { TaskProcess = creditPoint },
                _ => task,
            };
            tasks.Add(synced);
            if (synced == task)
            {
                continue;
            }

            changed.Add(synced);
            if (task.Status == TaskstatusComplete && synced.Status == TaskstatusRecved)
            {
                redDotSteps.Add(-1);
            }
            else if (synced.Status == TaskstatusComplete)
            {
                redDotSteps.AddRange(Enumerable.Repeat(1, CreditPointRewardBadgesPerCompletion));
            }

            logger.LogInformation(
                "Uid {Uid} credit-point task {TaskId}: status {Before} -> {After}, process {TaskProcess}, complete num {CompleteNum}",
                uid,
                task.TaskId,
                task.Status,
                synced.Status,
                synced.TaskProcess,
                synced.CompleteNum);
        }

        if (changed.Count == 0)
        {
            return ([.. tasks], []);
        }

        await repository.SaveTasksAsync(uid, changed);
        var pushes = new List<GamePacket>
        {
            GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, BuildUpdateTaskInfoNtf(changed)),
        };
        var badgeNum = await FindRedDotBadgeNumAsync(uid);
        foreach (var step in redDotSteps)
        {
            badgeNum = Math.Max(0, badgeNum + step);
            pushes.Add(TaskRedDots.BuildCount(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdCreditPointReward, badgeNum));
        }

        return ([.. tasks], [.. pushes]);
    }

    private static CreditPointTaskRecord Issue(CreditPointTaskDefinition definition, int creditPoint, long recvTime)
    {
        var reached = creditPoint >= definition.TargetProcess;
        return new CreditPointTaskRecord(
            definition.TaskId,
            reached ? TaskstatusComplete : TaskstatusRecved,
            Math.Min(creditPoint, definition.TargetProcess),
            reached ? 1 : NotCompleted,
            recvTime,
            reached ? recvTime : NoTime,
            NoTime);
    }

    private async Task<int> FindRedDotBadgeNumAsync(ulong uid) =>
        (await settings.LoadAsync(uid)).RedDotInfo.RedDotDetail
            .FirstOrDefault(redDot => redDot.RedDotID == CreditPointRewardRedDotID)?.RedDotBadgeNum ?? 0;

    private Task StoreRedDotAsync(ulong uid, int badgeNum) => badgeNum > 0
        ? settings.ReplaceRedDotsAsync(uid, [new pbCOMDT_RED_DOT_DETAIL { RedDotID = CreditPointRewardRedDotID, RedDotBadgeNum = badgeNum }])
        : settings.ClearRedDotAsync(uid, CreditPointRewardRedDotID);

    private static pbSCUpdateTaskInfoNtf BuildUpdateTaskInfoNtf(IEnumerable<CreditPointTaskRecord> tasks) => new()
    {
        TaskInfoList = { tasks.Select(ToTaskInfo) },
    };

    private static pbCOMDT_TASK_INFO ToTaskInfo(CreditPointTaskRecord task)
    {
        var taskInfo = new pbCOMDT_TASK_INFO
        {
            TaskId = task.TaskId,
            TaskSystem = TaskSystemCreditpoint,
            CompleteNum = task.CompleteNum,
            TaskProcess = { task.TaskProcess },
            Status = task.Status,
            RecvTime = task.RecvTime,
            CompleteTime = task.CompleteTime,
            RewardTime = task.RewardTime,
        };
        if (task.Status != TaskstatusRewarded)
        {
            taskInfo.NewTag = TaskNewTag;
        }

        return taskInfo;
    }

    private static pbSCResourcesAwardNotify BuildResourcesAwardNotify(IReadOnlyDictionary<int, long> coins) => new()
    {
        ResultInfo = new pbResultInfo(),
        AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTasksystemCreditpoint,
        ResourcesList =
        {
            coins.Select(coin => new pbCOMDT_ONE_RESOURCE_AWARD_REPLACE_INFO
            {
                FinallyAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO
                {
                    ResID = coin.Key,
                    ResCnt = (int)Math.Min(coin.Value, int.MaxValue),
                    ResType = (int)PB_ENUM_RES_RESOURCES_TYPE.PbResResourcesCoin,
                },
                OriginAwardInfo = new pbCOMDT_ONE_RESOURCE_AWARD_INFO(),
            }),
        },
    };

    private async Task<T> WithGateAsync<T>(ulong uid, Func<Task<T>> action)
    {
        var gate = gates.GetOrAdd(uid, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
