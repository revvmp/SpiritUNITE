using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class ActivityHandlers(
    RoleInfoStore roles,
    ActivityManager activityManager,
    ActivityProgressPushes activityProgressPushes,
    ActivityExchangeManager activityExchange,
    ILogger<ActivityHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRefreshActivityTaskReq)]
    public async Task<GamePacket[]> OnRefreshActivityTaskReq(GamePacket request)
    {
        pbCSRefreshActivityTaskReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        await activityManager.RecordLoginDayAsync(role.GameUlongUID);
        return [.. ActivityManager.BuildRefreshRedDotClears().Select(clear => request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRedDotClearNtf, clear))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClickTaskButtonReq)]
    public async Task<GamePacket[]> OnClickTaskButtonReq(GamePacket request)
    {
        var click = pbCSClickTaskButtonReq.Parser.ParseFrom(request.PkgData);
        if (!ActivityClientActions.IsValidButtonId(click.ButtonId))
        {
            logger.LogInformation("Task button {ButtonId} click refused for uid {Uid}: unknown button", click.ButtonId, request.Uid);
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClickTaskButtonRsp, new pbSCClickTaskButtonRsp
            {
                ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid },
            })];
        }

        var role = roles.Require(request.Uid);
        var progress = await activityManager.RecordClientActionAsync(role, ActivityClientActions.ClickedTaskButton(click.ButtonId));
        logger.LogInformation("Task button {ButtonId} clicked by uid {Uid}: {TaskCount} tasks progressed",
            click.ButtonId, request.Uid, progress.UpdatedTasks.Length);
        var reply = new ReplyList(request);
        reply.Add(PB_ENUM_CS_MSGID_DEF.PbScidClickTaskButtonRsp, new pbSCClickTaskButtonRsp { ResultInfo = new pbResultInfo() });
        reply.AddRange(await activityProgressPushes.BuildAsync(role, progress));
        if (progress.AutoClaim is { Awards.Count: > 0 } autoClaim)
        {
            reply.Add(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonTaskaward,
                ResourcesList = { autoClaim.Awards },
            });
        }

        return reply.ToArray();
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetActivityExchangeInfoReq)]
    public async Task<GamePacket[]> OnGetActivityExchangeInfoReq(GamePacket request)
    {
        pbCSGetActivityExchangeInfoReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var response = new pbSCGetActivityExchangeInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            AllExchangeInfo = await activityExchange.BuildAllExchangeInfoAsync(role),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetActivityExchangeInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidActivityExchangeReddotReq)]
    public async Task<GamePacket[]> OnActivityExchangeReddotReq(GamePacket request)
    {
        var reddot = pbCSActivityExchangeReddotReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await activityExchange.SetNotifyAsync(role, reddot.ActivityId, reddot.ExchangeId, reddot.Notify);
        logger.LogInformation(
            "Activity exchange {ExchangeId} notify {Notify} in activity {ActivityId} for uid {Uid}: {Result}",
            reddot.ExchangeId, reddot.Notify, reddot.ActivityId, request.Uid, outcome.Result);
        var response = new pbSCActivityExchangeReddotRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ActivityId = reddot.ActivityId,
            ExchangeId = reddot.ExchangeId,
            Notify = outcome.Notify,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidActivityExchangeReddotRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidDoExchangeReq)]
    public async Task<GamePacket[]> OnDoExchangeReq(GamePacket request)
    {
        var exchange = pbCSDoExchangeReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var outcome = await activityExchange.DoExchangeAsync(role, exchange.ActivityID, exchange.ExchangeID, exchange.DoExchangeNum);
        logger.LogInformation(
            "Activity exchange {ExchangeId} x{DoExchangeNum} in activity {ActivityId} for uid {Uid}: {Result}, done count {DoneCount}",
            exchange.ExchangeID, exchange.DoExchangeNum, exchange.ActivityID, request.Uid, outcome.Result, outcome.DoneCount);
        var response = new pbSCDoExchangeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            ActivityID = exchange.ActivityID,
            ExchangeID = exchange.ExchangeID,
            DoneCount = outcome.DoneCount,
        };
        var reply = new ReplyList(request);
        reply.AddRange(outcome.BeforeRsp);
        reply.Add(PB_ENUM_CS_MSGID_DEF.PbScidDoExchangeRsp, response);
        return reply.ToArray();
    }
}
