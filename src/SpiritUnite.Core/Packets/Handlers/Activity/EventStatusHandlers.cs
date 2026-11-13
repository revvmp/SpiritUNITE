using Microsoft.Extensions.Logging;
using SpiritUnite.Game.Activity;
using SpiritUnite.Game.PveChallenge;
using SpiritUnite.Game.Role;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Activity;

public sealed class EventStatusHandlers(
    ActivitySchedule schedule,
    RoleInfoStore roles,
    EeveeChallengeManager eeveeChallengeManager,
    PveChallengeManager pveChallengeManager,
    TimeProvider clock,
    ILogger<EventStatusHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidMysteryShopInfoReq)]
    public GamePacket[] OnMysteryShopInfoReq(GamePacket request)
    {
        pbCSMysteryShopInfoReq.Parser.ParseFrom(request.PkgData);
        var open = schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypeMysteryShop, clock.GetUtcNow());
        var response = new pbSCMysteryShopInfoRsp
        {
            ResultInfo = new pbResultInfo
            {
                ResultId = (int)(open is null
                    ? PB_ENUM_RES_ERROR_CODE.PbErrMysteryShopActivityNotExit
                    : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc),
            },
            ShopInfo = open is null
                ? new pbCOMDT_MYSTERY_SHOP_INFO()
                : new pbCOMDT_MYSTERY_SHOP_INFO
                {
                    ActivityID = open.Row.ActivityID,
                    StartTime = open.ValidStart.ToUnixTimeSeconds(),
                    EndTime = open.ValidEnd.ToUnixTimeSeconds(),
                },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidMysteryShopInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetPvePrivilegeInfoReq)]
    public async Task<GamePacket[]> OnGetPvePrivilegeInfoReq(GamePacket request)
    {
        var activityID = pbCSGetPvePrivilegeInfoReq.Parser.ParseFrom(request.PkgData).ActivityID;
        var result = schedule.IsOpen(activityID, PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypePvePrivilege, clock.GetUtcNow())
            ? PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
            : PB_ENUM_RES_ERROR_CODE.PbErrActivityNotExit;
        if (result != PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc)
        {
            logger.LogInformation("PVE privilege info for activity {ActivityID} refused for uid {Uid}: {Result}",
                activityID, request.Uid, result);
        }

        var response = new pbSCGetPvePrivilegeInfoRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            ActivityID = activityID,
            ActivityInfo = result == PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc
                ? await pveChallengeManager.BuildPrivilegeInfoAsync(roles.Require(request.Uid).GameUlongUID, activityID)
                : new pbCOMDT_PVE_PRIVILEGE_ACTIVITY_INFO(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetPvePrivilegeInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidPuzzleInfoReq)]
    public GamePacket[] OnPuzzleInfoReq(GamePacket request)
    {
        pbCSPuzzleInfoReq.Parser.ParseFrom(request.PkgData);
        var response = schedule.FindOpen(PB_ENUM_RES_ACTIVITY_TYPE.PbResActivityTypePuzzle, clock.GetUtcNow()) is null
            ? new pbSCPuzzleInfoRsp { ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed } }
            : new pbSCPuzzleInfoRsp { ResultInfo = new pbResultInfo(), PuzzleInfo = new pbCOMDT_PUZZLE_INFO() };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidPuzzleInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidDessertKitchenInfoReq)]
    public GamePacket[] OnDessertKitchenInfoReq(GamePacket request)
    {
        pbCSDessertKitchenInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCDessertKitchenInfoRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrCommonFailed },
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidDessertKitchenInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidEeveeChallengeStatusReq)]
    public async Task<GamePacket[]> OnEeveeChallengeStatusReq(GamePacket request)
    {
        pbCSEeveeChallengeStatusReq.Parser.ParseFrom(request.PkgData);
        var response = await eeveeChallengeManager.BuildStatusRspAsync(roles.Require(request.Uid));
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidEeveeChallengeStatusRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidEeveeChallengeDrawReq)]
    public async Task<GamePacket[]> OnEeveeChallengeDrawReq(GamePacket request)
    {
        var period = pbCSEeveeChallengeDrawReq.Parser.ParseFrom(request.PkgData).Period;
        var outcome = await eeveeChallengeManager.DrawAsync(roles.Require(request.Uid), period);
        var response = new pbSCEeveeChallengeDrawRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result },
            EeveeID = outcome.EeveeID,
        };
        return [.. AsReplies(request, outcome), request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidEeveeChallengeDrawRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidEeveeChallengeClickReq)]
    public async Task<GamePacket[]> OnEeveeChallengeClickReq(GamePacket request)
    {
        var period = pbCSEeveeChallengeClickReq.Parser.ParseFrom(request.PkgData).Period;
        var outcome = await eeveeChallengeManager.ClickAsync(roles.Require(request.Uid), period);
        var response = new pbSCEeveeChallengeClickRsp { ResultInfo = new pbResultInfo { ResultId = (int)outcome.Result } };
        return [.. AsReplies(request, outcome), request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidEeveeChallengeClickRsp, response)];
    }

    private static IEnumerable<GamePacket> AsReplies(GamePacket request, EeveeChallengeOutcome outcome) =>
        outcome.BeforeRsp.Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq });

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetActivityInfoReq)]
    public GamePacket[] OnGetActivityInfoReq(GamePacket request)
    {
        pbCSGetActivityInfoReq.Parser.ParseFrom(request.PkgData);
        var response = new pbSCGetActivityInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            OpenActivityList = new pbCOMDT_ACTIVITY_OPEN_LIST(),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetActivityInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidClearActivityNewTagReq)]
    public GamePacket[] OnClearActivityNewTagReq(GamePacket request)
    {
        var activityId = pbCSClearActivityNewTagReq.Parser.ParseFrom(request.PkgData).ActivityId;
        var response = new pbSCClearActivityNewTagRsp { ResultInfo = new pbResultInfo(), ActivityId = activityId };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidClearActivityNewTagRsp, response)];
    }
}
