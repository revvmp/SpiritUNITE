using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class ShareHandlers(
    RoleInfoStore roles,
    ActivityManager activityManager,
    ActivityProgressPushes activityProgressPushes,
    ILogger<ShareHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPlatformShareReq)]
    public GamePacket[] OnPlatformShareReq(GamePacket request)
    {
        var share = pbCSPlatformShareReq.Parser.ParseFrom(request.PkgData);
        logger.LogInformation("Platform share by uid {Uid}: type {PlatformShareType}, share {ShareId}, purpose {SharePurpose}, resource {ResID}",
            request.Uid, share.PlatformShareType, share.ShareId, share.SharePurpose, share.ResID);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPlatformShareRsp, new pbSCPlatformShareRsp
        {
            ResultInfo = new pbResultInfo(),
            PlatformShareType = share.PlatformShareType,
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidShareContentReportReq)]
    public async Task<GamePacket[]> OnShareContentReportReq(GamePacket request)
    {
        var report = pbCSShareContentReportReq.Parser.ParseFrom(request.PkgData);
        if (report.ShareType != (int)PB_ENUM_ENM_CLIENT_SHARE_TYPE.PbEnmClientShareTypeResource)
        {
            logger.LogInformation("Share content report refused for uid {Uid}: share type {ShareType}", request.Uid, report.ShareType);
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidShareContentReportRsp, new pbSCShareContentReportRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid },
            })];
        }

        var role = roles.Require(request.Uid);
        var progress = await activityManager.RecordClientActionAsync(role, ActivityClientActions.SharedAnyContent);
        logger.LogInformation("Share content report by uid {Uid}: resource type {ResourceType} id {ResourceID}, {TaskCount} tasks progressed",
            request.Uid, report.ShareContent?.ResourceShare?.ResourceType, report.ShareContent?.ResourceShare?.ResourceID, progress.UpdatedTasks.Length);
        var reply = new ReplyList(request);
        reply.Add(PB_ENUM_CS_MSGID_DEF.PbScidShareContentReportRsp, new pbSCShareContentReportRsp { ResultInfo = new pbResultInfo() });
        reply.AddRange(await activityProgressPushes.BuildAsync(role, progress));
        return reply.ToArray();
    }
}
