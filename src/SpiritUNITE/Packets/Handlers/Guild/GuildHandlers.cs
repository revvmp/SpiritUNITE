using Microsoft.Extensions.Logging;
using SpiritUNITE.Game.Activity;
using SpiritUNITE.Game.Guild;
using SpiritUNITE.Game.Role;
using SpiritUNITE.Packets.Messages;
using static SpiritUNITE.Packets.Messages.PB_ENUM_CS_MSGID_DEF;

namespace SpiritUNITE.Packets.Handlers.Guild;

public sealed class GuildHandlers(
    RoleInfoStore roles,
    GuildManager guilds,
    GuildComments comments,
    GuildActivityManager activities,
    ActivityRewardNotices rewardNotices,
    ILogger<GuildHandlers> logger)
{
    private const int MaxSearchTextLength = 64;
    private const int MaxAccusationCommentLength = (int)PB_MACRO_MAX_GUILD_COMMENT_ACCUSATION_CONTENT_LEN.PbMaxGuildCommentAccusationContentLen;

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildCreateReq)]
    public async Task<GamePacket[]> OnGuildCreateReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var create = pbCSGuildCreateReq.Parser.ParseFrom(request.PkgData);
        return Send(request, PbScidGuildCreateRsp, await guilds.CreateAsync(role, create.CreateGuildInfo));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildQueryReq)]
    public async Task<GamePacket[]> OnGuildQueryReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGuildQueryReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGuildQueryRsp, await guilds.QueryAsync(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildMemberListReq)]
    public async Task<GamePacket[]> OnGuildMemberListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGuildMemberListReq.Parser.ParseFrom(request.PkgData);
        return await guilds.MemberListAsync(request.Uid) is { } response
            ? [request.Reply(PbScidGuildMemberListRsp, response)]
            : [];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildSearchReq)]
    public async Task<GamePacket[]> OnGuildSearchReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var search = pbCSGuildSearchReq.Parser.ParseFrom(request.PkgData);
        if (search.ShortId.Length > MaxSearchTextLength || search.Name.Length > MaxSearchTextLength)
        {
            throw new InvalidDataException($"Guild search text of {search.ShortId.Length}/{search.Name.Length} characters is too long");
        }

        return [request.Reply(PbScidGuildSearchRsp, await guilds.SearchAsync(search.ShortId, search.Name))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildListSearchReq)]
    public async Task<GamePacket[]> OnGuildListSearchReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var search = pbCSGuildListSearchReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGuildListSearchRsp, await guilds.ListSearchAsync(request.Uid, [.. search.LabelList]))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildListSearchHistoryReq)]
    public async Task<GamePacket[]> OnGuildListSearchHistoryReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGuildListSearchHistoryReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGuildListSearchHistoryRsp, await guilds.ListSearchHistoryAsync(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildRecommendInfoReq)]
    public async Task<GamePacket[]> OnGuildRecommendInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGuildRecommendInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGuildRecommendInfoRsp, await guilds.RecommendAsync(request.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildQuerySearchGuildInfoReq)]
    public async Task<GamePacket[]> OnGuildQuerySearchGuildInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var query = pbCSGuildQuerySearchGuildInfoReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGuildQuerySearchGuildInfoRsp, await guilds.QuerySearchGuildInfoAsync(query.GuildId))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildJoinReq)]
    public async Task<GamePacket[]> OnGuildJoinReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var join = pbCSGuildJoinReq.Parser.ParseFrom(request.PkgData);
        return Send(request, PbScidGuildJoinRsp, await guilds.JoinAsync(role, join.GuildId));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildJoinRequestListReq)]
    public async Task<GamePacket[]> OnGuildJoinRequestListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGuildJoinRequestListReq.Parser.ParseFrom(request.PkgData);
        return Send(request, PbScidGuildJoinRequestListRsp, await guilds.JoinRequestListAsync(request.Uid));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildAcceptJoinRequestReq)]
    public async Task<GamePacket[]> OnGuildAcceptJoinRequestReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var accept = pbCSGuildAcceptJoinRequestReq.Parser.ParseFrom(request.PkgData);
        RequireFlag(accept.IsAcceptAll, nameof(accept.IsAcceptAll));
        return [request.Reply(PbScidGuildAcceptJoinRequestRsp, await guilds.AcceptJoinRequestAsync(request.Uid, accept.IsAcceptAll, accept.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildRejectJoinRequestReq)]
    public async Task<GamePacket[]> OnGuildRejectJoinRequestReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var reject = pbCSGuildRejectJoinRequestReq.Parser.ParseFrom(request.PkgData);
        RequireFlag(reject.IsRejectAll, nameof(reject.IsRejectAll));
        return Send(request, PbScidGuildRejectJoinRequestRsp, await guilds.RejectJoinRequestAsync(request.Uid, reject.IsRejectAll, reject.Uid));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildSettingReq)]
    public async Task<GamePacket[]> OnGuildSettingReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var setting = pbCSGuildSettingReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGuildSettingRsp, await guilds.SettingAsync(request.Uid, setting))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildTransferMasterReq)]
    public async Task<GamePacket[]> OnGuildTransferMasterReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var transfer = pbCSGuildTransferMasterReq.Parser.ParseFrom(request.PkgData);
        return Send(request, PbScidGuildTransferMasterRsp, await guilds.TransferMasterAsync(role, transfer.Uid));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildKickMemberReq)]
    public async Task<GamePacket[]> OnGuildKickMemberReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var kick = pbCSGuildKickMemberReq.Parser.ParseFrom(request.PkgData);
        return [request.Reply(PbScidGuildKickMemberRsp, await guilds.KickMemberAsync(request.Uid, kick.Uid))];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildMemberQuitReq)]
    public async Task<GamePacket[]> OnGuildMemberQuitReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        pbCSGuildMemberQuitReq.Parser.ParseFrom(request.PkgData);
        return Send(request, PbScidGuildMemberQuitRsp, await guilds.QuitAsync(role));
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildLeaveCommentReq)]
    public async Task<GamePacket[]> OnGuildLeaveCommentReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var leave = pbCSGuildLeaveCommentReq.Parser.ParseFrom(request.PkgData);
        if (await guilds.FindMembershipAsync(request.Uid) is not { } membership)
        {
            return [request.Reply(PbScidGuildLeaveCommentRsp, new pbSCGuildLeaveCommentRsp { ResultInfo = GuildManager.Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist) })];
        }

        var post = await comments.LeaveCommentAsync(membership.Member, role, leave);
        return
        [
            request.Reply(PbScidGuildLeaveCommentRsp, new pbSCGuildLeaveCommentRsp { ResultInfo = GuildManager.Result(post.Result) }),
            .. post.CommentNtf is { } commentNtf ? [commentNtf] : Array.Empty<GamePacket>(),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildCommentListReq)]
    public async Task<GamePacket[]> OnGuildCommentListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var list = pbCSGuildCommentListReq.Parser.ParseFrom(request.PkgData);
        var membership = await guilds.FindMembershipAsync(request.Uid);
        var response = new pbSCGuildCommentListRsp { ResultInfo = new pbResultInfo(), IsInGuild = GuildManager.InGuildFlag(membership) };
        if (membership is null)
        {
            return [request.Reply(PbScidGuildCommentListRsp, response)];
        }

        var commentNtf = await comments.BuildCommentNtfAsync(membership.Guild.GuildID, list.LastReceivedIndex);
        return
        [
            request.Reply(PbScidGuildCommentListRsp, response),
            .. commentNtf is null ? Array.Empty<GamePacket>() : [commentNtf],
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildRoomListReq)]
    public async Task<GamePacket[]> OnGuildRoomListReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSGuildRoomListReq.Parser.ParseFrom(request.PkgData);
        var result = await guilds.FindMembershipAsync(request.Uid) is null
            ? PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist
            : PB_ENUM_RES_ERROR_CODE.PbErrCommonSucc;
        return [request.Reply(PbScidGuildRoomListRsp, new pbSCGuildRoomListRsp { ResultInfo = GuildManager.Result(result) })];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidQueryGuildActivityInfoReq)]
    public async Task<GamePacket[]> OnQueryGuildActivityInfoReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSQueryGuildActivityInfoReq.Parser.ParseFrom(request.PkgData);
        if (await guilds.FindMembershipAsync(request.Uid) is not { } membership)
        {
            return [request.Reply(PbScidQueryGuildActivityInfoRsp, new pbSCQueryGuildActivityInfoRsp { ResultInfo = GuildManager.Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist) })];
        }

        return
        [
            request.Reply(PbScidQueryGuildActivityInfoRsp, new pbSCQueryGuildActivityInfoRsp
            {
                ResultInfo = new pbResultInfo(),
                GuildActivityInfo = await activities.BuildActivityInfoAsync(request.Uid, membership.Guild.GuildID),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidQueryGuildScoreReq)]
    public async Task<GamePacket[]> OnQueryGuildScoreReq(GamePacket request)
    {
        roles.Require(request.Uid);
        pbCSQueryGuildScoreReq.Parser.ParseFrom(request.PkgData);
        if (await guilds.FindMembershipAsync(request.Uid) is not { } membership)
        {
            return [request.Reply(PbScidQueryGuildScoreRsp, new pbSCQueryGuildScoreRsp { ResultInfo = GuildManager.Result(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist) })];
        }

        return
        [
            request.Reply(PbScidQueryGuildScoreRsp, new pbSCQueryGuildScoreRsp
            {
                ResultInfo = new pbResultInfo(),
                Score = await activities.FindGuildScoreAsync(membership.Guild.GuildID),
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidSelectGuildRewardReq)]
    public async Task<GamePacket[]> OnSelectGuildRewardReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var select = pbCSSelectGuildRewardReq.Parser.ParseFrom(request.PkgData);
        var change = await guilds.FindMembershipAsync(request.Uid) is { } membership
            ? await activities.SelectRewardAsync(membership.Member, select.RewardID)
            : new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist, 0);
        return
        [
            request.Reply(PbScidSelectGuildRewardRsp, new pbSCSelectGuildRewardRsp
            {
                ResultInfo = GuildManager.Result(change.Result),
                RewardID = change.RewardID,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidCancelGuildRewardReq)]
    public async Task<GamePacket[]> OnCancelGuildRewardReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var cancel = pbCSCancelGuildRewardReq.Parser.ParseFrom(request.PkgData);
        var change = await guilds.FindMembershipAsync(request.Uid) is { } membership
            ? await activities.CancelRewardAsync(membership.Member, cancel.RewardID)
            : new GuildActivityRewardChange(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist, cancel.RewardID);
        return
        [
            request.Reply(PbScidCancelGuildRewardRsp, new pbSCCancelGuildRewardRsp
            {
                ResultInfo = GuildManager.Result(change.Result),
                RewardID = change.RewardID,
            }),
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGetGuildScoreRewardReq)]
    public async Task<GamePacket[]> OnGetGuildScoreRewardReq(GamePacket request)
    {
        var role = roles.Require(request.Uid);
        var claim = pbCSGetGuildScoreRewardReq.Parser.ParseFrom(request.PkgData);
        var reward = await guilds.FindMembershipAsync(request.Uid) is { } membership
            ? await activities.GetScoreRewardAsync(role, membership.Member, claim.AcntRewardGear)
            : GuildRewardClaim.Refused(PB_ENUM_RES_ERROR_CODE.PbErrGuildMemberNotExist);
        var response = request.Reply(PbScidGetGuildScoreRewardRsp, new pbSCGetGuildScoreRewardRsp
        {
            ResultInfo = GuildManager.Result(reward.Result),
            AcntRewardGear = claim.AcntRewardGear,
        });
        if (reward.Rewards is not { } rewards || rewards.Awards.Count == 0)
        {
            return [response];
        }

        return
        [
            .. (await rewardNotices.BuildAsync(role, rewards)).Select(packet => packet with { Uid = request.Uid, ClientSeq = request.ClientSeq }),
            request.Reply(PbScidResourcesAwardNotify, new pbSCResourcesAwardNotify
            {
                ResultInfo = new pbResultInfo(),
                AwardReason = (int)PB_ENUM_ENM_RESOURCE_CHG_REASON.PbEnmResourceChgReasonGuildActivity,
                ResourcesList = { rewards.Awards },
            }),
            response,
        ];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PbCsidGuildAccusationReq)]
    public GamePacket[] OnGuildAccusationReq(GamePacket request)
    {
        roles.Require(request.Uid);
        var accusation = pbCSGuildAccusationReq.Parser.ParseFrom(request.PkgData);
        var info = accusation.AccusationInfo;
        if (info?.AcntComment.Length > MaxAccusationCommentLength)
        {
            throw new InvalidDataException($"Guild accusation comment of {info.AcntComment.Length} characters is too long");
        }

        logger.LogInformation(
            "Uid {Uid} reported guild {GuildID} (accusation type {AccusationType}, reasons {Reasons}); moderation is not implemented",
            request.Uid,
            info?.CommonData?.GuildInfo?.BriefInfo?.GuildID ?? 0,
            info?.AccusationType ?? 0,
            string.Join(",", info?.CommonData?.ReasonList ?? []));
        return [request.Reply(PbScidGuildAccusationRsp, new pbSCGuildAccusationRsp { ResultInfo = new pbResultInfo() })];
    }

    private static GamePacket[] Send<TResponse>(GamePacket request, PB_ENUM_CS_MSGID_DEF responseID, GuildOutcome<TResponse> outcome)
        where TResponse : Google.Protobuf.IMessage =>
        [request.Reply(responseID, outcome.Response), .. outcome.Notices];

    private static void RequireFlag(int flag, string name)
    {
        if (flag is not (0 or 1))
        {
            throw new InvalidDataException($"Guild request flag {name} has value {flag}");
        }
    }
}
