using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers;

public sealed class TaskHandlers(RoleInfoStore roles, ActivityTasks activityTasks)
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
            TaskSysExtraInfo = new pbCOMDT_TASK_SYSTEM_EXTRA_INFO(),
        };
        if (query.ModuleType == (int)PB_ENUM_ENM_CLIENT_TASKMODULE.PbEnmClientTaskmoduleActivity)
        {
            var role = roles.Require(request.Uid);
            await activityTasks.RecordLoginDayAsync(role.GameUlongUID);
            response.TaskInfoList.Add(await activityTasks.BuildTaskInfoListAsync(role.GameUlongUID));
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetTaskInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetTaskRewardReq)]
    public async Task<GamePacket[]> OnGetTaskRewardReq(GamePacket request)
    {
        var claimRequest = pbCSGetTaskRewardReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var claim = claimRequest.IsBatch == SingleTask
            ? await activityTasks.ClaimAsync(role, claimRequest.TaskId)
            : await activityTasks.ClaimGroupAsync(role, claimRequest.GroupID);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetTaskRewardRsp, new pbSCGetTaskRewardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)claim.Result },
            TaskId = claimRequest.TaskId,
            IsBatch = claimRequest.IsBatch,
            ModuleType = claimRequest.ModuleType,
            GroupID = claimRequest.GroupID,
        });
        if (claim.UpdatedTasks.Length == 0)
        {
            return [response];
        }

        var update = new pbSCUpdateTaskInfoNtf { TaskInfoList = { claim.UpdatedTasks } };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateTaskInfoNtf, update), response];
    }
}
