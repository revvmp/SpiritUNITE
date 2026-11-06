using Microsoft.Extensions.Logging;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Ranked;

public sealed class RankedMatchHandlers(
    RoleInfoStore roles,
    GameData gameData,
    LobbyNotices notices,
    TimeProvider clock,
    ILogger<RankedMatchHandlers> logger)
{
    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchInfoReq)]
    public GamePacket[] OnGetRankedMatchInfoReq(GamePacket request)
    {
        pbCSGetRankedMatchInfoReq.Parser.ParseFrom(request.PkgData);
        if (!IsRankedMatchUnlocked(request, "info"))
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchInfoRsp, new pbSCGetRankedMatchInfoRsp
            {
                ResultInfo = RankedMatchLevelLimit,
            })];
        }

        var response = new pbSCGetRankedMatchInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            Detail = RankedMatch.BuildStartingDetail(gameData, clock.GetUtcNow()),
        };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchInfoRsp, response)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRefreshRankedMatchInfoReq)]
    public GamePacket[] OnRefreshRankedMatchInfoReq(GamePacket request)
    {
        pbCSRefreshRankedMatchInfoReq.Parser.ParseFrom(request.PkgData);
        var refreshed = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRefreshRankedMatchInfoRsp, new pbSCRefreshRankedMatchInfoRsp());
        if (!IsRankedMatchUnlocked(request, "refresh"))
        {
            return [refreshed];
        }

        var notify = new pbSCRankedMatchInfoNtf { Detail = RankedMatch.BuildStartingDetail(gameData, clock.GetUtcNow()) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRankedMatchInfoNtf, notify), refreshed];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchSeasonRewardReq)]
    public GamePacket[] OnGetRankedMatchSeasonRewardReq(GamePacket request)
    {
        pbCSGetRankedMatchSeasonRewardReq.Parser.ParseFrom(request.PkgData);
        if (!IsRankedMatchUnlocked(request, "season reward"))
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchSeasonRewardRsp, new pbSCGetRankedMatchSeasonRewardRsp())];
        }

        logger.LogInformation("Ranked season reward for uid {Uid}: no ranked season was played, nothing awarded", request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchSeasonRewardRsp, new pbSCGetRankedMatchSeasonRewardRsp())];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchSeasonTaskRewardReq)]
    public GamePacket[] OnGetRankedMatchSeasonTaskRewardReq(GamePacket request)
    {
        var claim = pbCSGetRankedMatchSeasonTaskRewardReq.Parser.ParseFrom(request.PkgData);
        if (!IsRankedMatchUnlocked(request, "season task award"))
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchSeasonTaskRewardRsp, new pbSCGetRankedMatchSeasonTaskRewardRsp
            {
                ResultInfo = RankedMatchLevelLimit,
            })];
        }

        var result = RankedMatch.RefuseSeasonTaskAward(claim.RankedDivision);
        logger.LogInformation(
            "Ranked season task award {RankedDivision} refused for uid {Uid}: {Result}; every account is still at the starting division",
            claim.RankedDivision, request.Uid, result);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchSeasonTaskRewardRsp, new pbSCGetRankedMatchSeasonTaskRewardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchHistoryInfoReq)]
    public GamePacket[] OnGetRankedMatchHistoryInfoReq(GamePacket request)
    {
        pbCSGetRankedMatchHistoryInfoReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchHistoryInfoRsp, new pbSCGetRankedMatchHistoryInfoRsp
        {
            ResultInfo = new pbResultInfo(),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankingListReq)]
    public GamePacket[] OnGetRankingListReq(GamePacket request)
    {
        var query = pbCSGetRankingListReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankingListRsp, new pbSCGetRankingListRsp
        {
            ResultInfo = new pbResultInfo(),
            TopBaseInfo = query.TopBaseInfo ?? new pbCOMDT_RANKING_BASE_INFO(),
            RankingList = new pbCOMDT_RANKING_LIST_SUCC(),
            CreditCheckInfo = new pbCOMDT_COMMON_CREDIT_CHECK_INFO { CreditInfo = new pbCOMDT_COMMON_CREDIT_INFO() },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAcntRankingInfoReq)]
    public GamePacket[] OnGetAcntRankingInfoReq(GamePacket request)
    {
        var query = pbCSGetAcntRankingInfoReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var baseInfo = query.TopBaseInfo ?? new pbCOMDT_RANKING_BASE_INFO();
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAcntRankingInfoRsp, new pbSCGetAcntRankingInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            AcntRankingDetail = new pbCOMDT_ACNT_RANKING_DETAIL
            {
                ScoreType = baseInfo.ScoreType,
                SubTypeList = { baseInfo.SubTypeList },
            },
        })];
    }

    private static pbResultInfo RankedMatchLevelLimit =>
        new() { ResultId = (int)PB_ENUM_RES_ERROR_CODE.PbErrRankedMatchLevelLimit };

    private bool IsRankedMatchUnlocked(GamePacket request, string requestKind)
    {
        var role = roles.Require(request.Uid);
        if (RankedMatch.IsRankedMatchUnlocked(gameData, notices, role))
        {
            return true;
        }

        logger.LogInformation(
            "Ranked {RequestKind} refused for uid {Uid}: trainer exp {RoleTotalExp} is below the ranked unlock",
            requestKind, request.Uid, role.RoleTotalExp);
        return false;
    }
}
