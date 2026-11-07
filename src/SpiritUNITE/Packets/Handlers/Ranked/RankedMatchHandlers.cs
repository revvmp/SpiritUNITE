using Microsoft.Extensions.Logging;
using SpiritUNITE.Database.Repositories;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Bag;
using SpiritUNITE.Game.Lobby;
using SpiritUNITE.Game.Ranked;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Packets.Handlers.Ranked;

public sealed class RankedMatchHandlers(
    RoleInfoStore roles,
    RankedManager ranked,
    RankedRankingBoard rankingBoard,
    GameData gameData,
    LobbyNotices notices,
    ILogger<RankedMatchHandlers> logger)
{
    private const int RankedMatchAwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonRankedMatchAward;
    private const int RankedMatchTaskAwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonRankedMatchTaskAward;

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchInfoReq)]
    public async Task<GamePacket[]> OnGetRankedMatchInfoReq(GamePacket request)
    {
        pbCSGetRankedMatchInfoReq.Parser.ParseFrom(request.PkgData);
        if (!IsRankedMatchUnlocked(request, "info"))
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchInfoRsp, new pbSCGetRankedMatchInfoRsp
            {
                ResultInfo = RankedMatchLevelLimit,
            })];
        }

        return [.. await BuildRankedInfoAsync(request, roles.Require(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRefreshRankedMatchInfoReq)]
    public async Task<GamePacket[]> OnRefreshRankedMatchInfoReq(GamePacket request)
    {
        pbCSRefreshRankedMatchInfoReq.Parser.ParseFrom(request.PkgData);
        var refreshed = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRefreshRankedMatchInfoRsp, new pbSCRefreshRankedMatchInfoRsp());
        if (!IsRankedMatchUnlocked(request, "refresh"))
        {
            return [refreshed];
        }

        var role = roles.Require(request.Uid);
        if (await ranked.FindSeasonStatusAsync(request.Uid) != RankedSeasonStatus.Current)
        {
            return [.. await BuildRankedInfoAsync(request, role), refreshed];
        }

        var notify = new pbSCRankedMatchInfoNtf { Detail = await ranked.BuildDetailAsync(request.Uid) };
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRankedMatchInfoNtf, notify), refreshed];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchSeasonRewardReq)]
    public async Task<GamePacket[]> OnGetRankedMatchSeasonRewardReq(GamePacket request)
    {
        pbCSGetRankedMatchSeasonRewardReq.Parser.ParseFrom(request.PkgData);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchSeasonRewardRsp, new pbSCGetRankedMatchSeasonRewardRsp());
        if (!IsRankedMatchUnlocked(request, "season reward"))
        {
            return [response];
        }

        var role = roles.Require(request.Uid);
        if (await ranked.FindSeasonStatusAsync(request.Uid) == RankedSeasonStatus.Current
            || await ranked.RolloverSeasonAsync(role) is not { } rollover)
        {
            logger.LogInformation("Ranked season reward for uid {Uid}: no previous season awaits its results, nothing awarded", request.Uid);
            return [response];
        }

        return
        [
            .. BuildRolloverPackets(request, role, rollover),
            response,
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchInfoRsp, new pbSCGetRankedMatchInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                Detail = await ranked.BuildDetailAsync(request.Uid),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchSeasonTaskRewardReq)]
    public async Task<GamePacket[]> OnGetRankedMatchSeasonTaskRewardReq(GamePacket request)
    {
        var claimRequest = pbCSGetRankedMatchSeasonTaskRewardReq.Parser.ParseFrom(request.PkgData);
        if (!IsRankedMatchUnlocked(request, "season task award"))
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchSeasonTaskRewardRsp, new pbSCGetRankedMatchSeasonTaskRewardRsp
            {
                ResultInfo = RankedMatchLevelLimit,
            })];
        }

        var role = roles.Require(request.Uid);
        var claim = await ranked.ClaimSeasonTaskAwardAsync(role, claimRequest.RankedDivision);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchSeasonTaskRewardRsp, new pbSCGetRankedMatchSeasonTaskRewardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)claim.Result },
        });
        if (claim is not { Grant: { } grant, Commit: { } commit })
        {
            return [response];
        }

        return
        [
            .. BuildGrantPackets(request, role, grant, commit, RankedMatchTaskAwardReason),
            request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRankedMatchInfoNtf, new pbSCRankedMatchInfoNtf
            {
                Detail = await ranked.BuildDetailAsync(request.Uid),
            }),
            response,
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankedMatchHistoryInfoReq)]
    public async Task<GamePacket[]> OnGetRankedMatchHistoryInfoReq(GamePacket request)
    {
        pbCSGetRankedMatchHistoryInfoReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchHistoryInfoRsp, await ranked.BuildHistoryAsync(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidProtectCardSetSelectReq)]
    public async Task<GamePacket[]> OnProtectCardSetSelectReq(GamePacket request)
    {
        var select = pbCSProtectCardSetSelectReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var result = await ranked.SetProtectCardSelectStatusAsync(request.Uid, select.SelectStatus);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidProtectCardSetSelectRsp, new pbSCProtectCardSetSelectRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
            SelectStatus = ranked.ProtectCardSelectStatusOf(request.Uid),
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidSetNoRemindedBuyProtectCardReq)]
    public async Task<GamePacket[]> OnSetNoRemindedBuyProtectCardReq(GamePacket request)
    {
        var reminder = pbCSSetNoRemindedBuyProtectCardReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        var result = await ranked.SetNoRemindedBuyProtectCardAsync(request.Uid, reminder.IsTodaySetNoReminded);
        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidSetNoRemindedBuyProtectCardRsp, new pbSCSetNoRemindedBuyProtectCardRsp
        {
            ResultInfo = new pbResultInfo { ResultId = (int)result },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetRankingListReq)]
    public async Task<GamePacket[]> OnGetRankingListReq(GamePacket request)
    {
        var query = pbCSGetRankingListReq.Parser.ParseFrom(request.PkgData);
        roles.Require(request.Uid);
        if (query.TopBaseInfo?.ScoreType == RankedRankingBoard.ScoreTypeRankedDivision)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankingListRsp, await rankingBoard.BuildRankingListAsync(query.TopBaseInfo))];
        }

        return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankingListRsp, new pbSCGetRankingListRsp
        {
            ResultInfo = new pbResultInfo(),
            TopBaseInfo = query.TopBaseInfo ?? new pbCOMDT_RANKING_BASE_INFO(),
            RankingList = new pbCOMDT_RANKING_LIST_SUCC(),
            CreditCheckInfo = new pbCOMDT_COMMON_CREDIT_CHECK_INFO { CreditInfo = new pbCOMDT_COMMON_CREDIT_INFO() },
        })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidGetAcntRankingInfoReq)]
    public async Task<GamePacket[]> OnGetAcntRankingInfoReq(GamePacket request)
    {
        var query = pbCSGetAcntRankingInfoReq.Parser.ParseFrom(request.PkgData);
        var role = roles.Require(request.Uid);
        var baseInfo = query.TopBaseInfo ?? new pbCOMDT_RANKING_BASE_INFO();
        if (baseInfo.ScoreType == RankedRankingBoard.ScoreTypeRankedDivision)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetAcntRankingInfoRsp, new pbSCGetAcntRankingInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                AcntRankingDetail = await rankingBoard.BuildAcntRankingDetailAsync(role),
            })];
        }

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

    private async Task<List<GamePacket>> BuildRankedInfoAsync(GamePacket request, LMNRoleInfo role)
    {
        var uid = request.Uid;
        var packets = new List<GamePacket>();
        var status = await ranked.FindSeasonStatusAsync(uid);
        if (status == RankedSeasonStatus.AwaitingSeasonRewardConfirm && await ranked.BuildPendingSeasonDetailAsync(uid) is { } pendingDetail)
        {
            logger.LogInformation(
                "Ranked season {SeasonId} of uid {Uid} awaits its season reward confirmation (GetRankedMatchSeasonRewardReq)",
                pendingDetail.CurrentRankedDivision.SeasonId,
                uid);
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchInfoRsp, new pbSCGetRankedMatchInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                Detail = pendingDetail,
            }));
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRankedMatchInfoNtf, new pbSCRankedMatchInfoNtf { Detail = pendingDetail.Clone() }));
            return packets;
        }

        if (status == RankedSeasonStatus.AwaitingSeasonRollover && await ranked.RolloverSeasonAsync(role) is { } rollover)
        {
            packets.AddRange(BuildRolloverPackets(request, role, rollover));
            var seasonOverDetail = await ranked.BuildDetailAsync(uid);
            seasonOverDetail.RankedMatchState = (int)PB_ENUM_COM_RANK_STATE_TYPE.PbComRankStateOfSeasonover;
            packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRankedMatchInfoNtf, new pbSCRankedMatchInfoNtf { Detail = seasonOverDetail }));
        }

        packets.Add(request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetRankedMatchInfoRsp, new pbSCGetRankedMatchInfoRsp
        {
            ResultInfo = new pbResultInfo(),
            Detail = await ranked.BuildDetailAsync(uid),
        }));
        return packets;
    }

    private IEnumerable<GamePacket> BuildRolloverPackets(GamePacket request, LMNRoleInfo role, RankedSeasonRollover rollover) =>
    [
        .. rollover.SeasonAward.IsEmpty
            ? []
            : BuildGrantPackets(request, role, rollover.SeasonAward, rollover.Commit, RankedMatchAwardReason),
        request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRankedMatchSeasonInheritNtf, new pbSCRankedMatchSeasonInheritNtf
        {
            OldRankedDivision = rollover.OldRankedDivision,
            NewRankedDivision = rollover.NewRankedDivision,
        }),
    ];

    private IEnumerable<GamePacket> BuildGrantPackets(GamePacket request, LMNRoleInfo role, RankedGrant grant, RankedGrantCommit commit, int awardReason)
    {
        foreach (var item in grant.Items.Where(item => item.ItemType == RoleItemType.AvatarItem))
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidUpdateAvatarNtf, new pbSCUpdateAvatarNtf
            {
                UpdateType = (int)PB_ENUM_ENM_AVATAR_UPDATETYPE.PbEnmAvatarUpdatetypeGain,
                AvatarInfo = item.ToAvatarInfo(),
            });
        }

        if (grant.Items.Any(item => item.ItemType == RoleItemType.LobbyBg))
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetLobbyBackgroundInfoRsp, new pbSCGetLobbyBackgroundInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                LobbyBackgroundInfo = role.BuildLobbyBackgroundInfo(gameData),
            });
        }

        if (commit.ChangedItems.Count > 0)
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidOutSideItemUpdateNotify, BagManager.BuildOutSideItemUpdateNotify(commit.ChangedItems));
        }

        if (grant.Change.ChangesCoins && commit.Wallet is { } wallet)
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidGetCoinValNtf, new pbSCGetCoinValNtf { CoinInfo = wallet.BuildCoinInfo() });
        }

        if (grant.Awards.Count > 0)
        {
            yield return request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = awardReason,
                ResourcesList = { grant.Awards.Select(award => award.Clone()) },
            });
        }
    }

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
