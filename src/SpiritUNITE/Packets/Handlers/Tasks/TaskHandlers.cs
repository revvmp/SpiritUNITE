using SpiritUNITE.Game.Achievement;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Newbie;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Game.StarRoad;
using SpiritUNITE.Game.Tasks;
using SpiritUNITE.Game.Wallet;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Tasks;

public sealed class TaskHandlers(
    RoleInfoStore roles,
    ActivityTasks activityTasks,
    AchievementTasks achievementTasks,
    NewbieChallengeTasks newbieChallengeTasks,
    TaskUpdates taskUpdates,
    ActivityRewardNotices rewardNotices)
{
    private const int SingleTask = 0;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetTaskInfoReq)]
    public async Task<GamePacket[]> OnGetTaskInfoReq(GamePacket request)
    {
        var query = pbCSGetTaskInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetTaskInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            ModuleType = query.ModuleType,
        };
        var moduleType = (PB_ENUM_ENM_CLIENT_TASKMODULE)query.ModuleType;
        switch (moduleType)
        {
            case PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleActivity:
                var role = roles.Require(request.Uid);
                await activityTasks.RecordLoginDayAsync(role.GameUlongUID);
                response.TaskInfoList.Add(await activityTasks.BuildTaskInfoListAsync(role));
                break;
            case PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleAchievement:
                response.TaskInfoList.Add(await achievementTasks.BuildTaskInfoListAsync(roles.Require(request.Uid)));
                break;
            case PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleNewbiechallenge:
                response.TaskInfoList.Add(await newbieChallengeTasks.BuildTaskInfoListAsync(roles.Require(request.Uid)));
                break;
        }

        return
        [
            .. TaskRedDots.BuildTaskInfoRedDots(moduleType, response.TaskInfoList)
                .Select(redDot => redDot with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetTaskInfoRsp, response),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetTaskRewardReq)]
    public async Task<GamePacket[]> OnGetTaskRewardReq(GamePacket request)
    {
        var claimRequest = pbCSGetTaskRewardReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        if (claimRequest.ModuleType == (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemAchievement)
        {
            return await ClaimAchievementAsync(request, claimRequest, role);
        }

        if (claimRequest.ModuleType == (int)PB_ENUM_ENM_TASKSYSTEM.PbEnmTasksystemNewbiechallenge)
        {
            return await ClaimNewbieChallengeAsync(request, claimRequest, role);
        }

        var achievementCompleteCountBefore = await achievementTasks.CountCompleteTasksAsync(role);
        var claim = claimRequest.IsBatch == SingleTask
            ? await activityTasks.ClaimAsync(role, claimRequest.TaskId)
            : await activityTasks.ClaimGroupAsync(role, claimRequest.GroupID);
        var response = BuildRewardResponse(request, claimRequest, claim.Result);
        if (claim.UpdatedTasks.Length == 0)
        {
            return [response];
        }

        var taskUpdate = BuildUpdateTaskInfoNtf(request, claim.UpdatedTasks);
        var redDot = BuildActivityRewardRedDot(request, claim.CompleteTaskCount);
        var chained = claim.ChainedTasks.Length > 0;
        GamePacket[] rewards =
        [
            .. await BuildRewardGroupPacketsAsync(request, role, claim.Rewards),
            .. chained
                ? [BuildUpdateTaskInfoNtf(request, claim.ChainedTasks), .. await BuildRewardGroupPacketsAsync(request, role, claim.ChainedRewards)]
                : Array.Empty<GamePacket>(),
            .. claim.Awards.Count > 0
                ? new[]
                {
                    request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
                    {
                        ResultInfo = new pbResultInfo(),
                        AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTaskaward,
                        ResourcesList = { claim.Awards },
                    }),
                }
                : [],
            .. claim.ProgressedTasks.Length > 0 ? new[] { BuildUpdateTaskInfoNtf(request, claim.ProgressedTasks) } : [],
            .. chained
                ? new[]
                {
                    BuildActivityRewardRedDot(request, claim.CompleteTaskCount + claim.ChainedTasks.Length),
                    BuildActivityRewardRedDot(request, claim.CompleteTaskCount),
                }
                : [],
            .. claim.Awards.Count > 0
                ? [
                    request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, await taskUpdates.BuildOwnershipUpdateTaskInfoNtfAsync(role)),
                    .. await BuildAchievementRedDotAsync(request, role, achievementCompleteCountBefore),
                ]
                : Array.Empty<GamePacket>(),
        ];
        return claimRequest.IsBatch == SingleTask
            ? [taskUpdate, redDot, .. rewards, response]
            : [.. rewards, taskUpdate, redDot, response];
    }

    private async Task<GamePacket[]> BuildRewardGroupPacketsAsync(GamePacket request, LMNRoleInfo role, ActivityRewardGroup? rewards) =>
        [.. (await rewardNotices.BuildAsync(role, rewards)).Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq })];

    private async Task<GamePacket[]> BuildAchievementRedDotAsync(GamePacket request, LMNRoleInfo role, int completeCountBefore)
    {
        var completeCount = await achievementTasks.CountCompleteTasksAsync(role);
        return completeCount > completeCountBefore
            ? [TaskRedDots.BuildCount(PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdAchievement, completeCount) with { Uid = request.Uid, ClientSeq = request.ClientSeq }]
            : [];
    }

    private static GamePacket BuildUpdateTaskInfoNtf(GamePacket request, pbCOMDT_TASK_INFO[] tasks) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, new pbSCUpdateTaskInfoNtf { TaskInfoList = { tasks } });

    private static GamePacket BuildActivityRewardRedDot(GamePacket request, int completeTaskCount) => completeTaskCount > 0
        ? request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRedDotReplaceNtf, new pbSCRedDotReplaceNtf
        {
            RedDotInfo = new pbCOMDT_RED_DOT_INFO
            {
                RedDotDetail =
                {
                    new pbCOMDT_RED_DOT_DETAIL
                    {
                        RedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdActivityReward,
                        RedDotBadgeNum = completeTaskCount,
                    },
                },
            },
        })
        : request.Reply(
            PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf,
            new pbSCRedDotClearNtf { RedDotID = (int)PB_ENUM_RES_RED_DOT_ID.PbResRedDotIdActivityReward });

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearTaskNewTagReq)]
    public GamePacket[] OnClearTaskNewTagReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var clear = pbCSClearTaskNewTagReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearTaskNewTagRsp, new pbSCClearTaskNewTagRsp
        {
            ResultInfo = new pbResultInfo(),
            TaskId = clear.TaskId,
            IsBatch = clear.IsBatch,
            ModuleType = clear.ModuleType,
        })];
    }

    private async Task<GamePacket[]> ClaimAchievementAsync(
        GamePacket request,
        pbCSGetTaskRewardReq claimRequest,
        LMNRoleInfo role)
    {
        var claim = claimRequest.IsBatch == SingleTask
            ? await achievementTasks.ClaimAsync(role, claimRequest.TaskId)
            : AchievementClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        var response = BuildRewardResponse(request, claimRequest, claim.Result);
        if (claim is not { TaskInfo: { } taskInfo, Reward: { } reward, Wallet: { } wallet })
        {
            return [response];
        }

        return
        [
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, new pbSCUpdateTaskInfoNtf { TaskInfoList = { taskInfo } }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidAchievementCompleteNtf, new pbSCAchievementCompleteNtf
            {
                Score = claim.Score,
                TaskInfo = taskInfo,
            }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() }),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, reward.BuildResourcesAwardNotify()),
            response,
        ];
    }

    private async Task<GamePacket[]> ClaimNewbieChallengeAsync(
        GamePacket request,
        pbCSGetTaskRewardReq claimRequest,
        LMNRoleInfo role)
    {
        var claim = claimRequest.IsBatch == SingleTask
            ? await newbieChallengeTasks.ClaimAsync(role, claimRequest.TaskId)
            : NewbieChallengeClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid);
        return
        [
            .. claim.Pushes.Select(push => push with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            BuildRewardResponse(request, claimRequest, claim.Result),
        ];
    }

    private static GamePacket BuildRewardResponse(
        GamePacket request,
        pbCSGetTaskRewardReq claimRequest,
        PB_ENUM_RES_ERROR_CODE result) =>
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetTaskRewardRsp, new pbSCGetTaskRewardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            TaskId = claimRequest.TaskId,
            IsBatch = claimRequest.IsBatch,
            ModuleType = claimRequest.ModuleType,
            GroupID = claimRequest.GroupID,
        });
}
