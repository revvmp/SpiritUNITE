using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Activity;

public sealed class ActivityHandlers(RoleInfoStore roles, ActivityManager activityManager, ILogger<ActivityHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRefreshActivityTaskReq)]
    public async Task<GamePacket[]> OnRefreshActivityTaskReq(GamePacket request)
    {
        pbCSRefreshActivityTaskReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        await activityManager.RecordLoginDayAsync(role.GameUlongUID);
        return [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetActivityExchangeInfoReq)]
    public GamePacket[] OnGetActivityExchangeInfoReq(GamePacket request)
    {
        pbCSGetActivityExchangeInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetActivityExchangeInfoRsp
        {
            AllExchangeInfo = new pbCOMDT_ALL_EXCHANGE_INFO(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetActivityExchangeInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidDoExchangeReq)]
    public GamePacket[] OnDoExchangeReq(GamePacket request)
    {
        var exchange = pbCSDoExchangeReq.Parser.ParseFrom(request.PkgData);
        const PB_ENUM_RES_ERROR_CODE result = PB_ENUM_RES_ERROR_CODE.PbErrActivityExchangeCheckFail;
        logger.LogInformation(
            "Activity exchange {ExchangeId} x{DoExchangeNum} in activity {ActivityId} refused for uid {Uid}: {Result}",
            exchange.ExchangeID, exchange.DoExchangeNum, exchange.ActivityID, request.Uid, result);
        var response = new pbSCDoExchangeRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            ActivityID = exchange.ActivityID,
            ExchangeID = exchange.ExchangeID,
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidDoExchangeRsp, response)];
    }
}
