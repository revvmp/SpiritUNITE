using Microsoft.Extensions.Logging;
using SpiritUnite.Battle;
using SpiritUnite.Game.Credit;
using SpiritUnite.Game.Friend;
using SpiritUnite.Game.Match;
using SpiritUnite.Game.Ranked;
using SpiritUnite.Game.Room;
using SpiritUnite.Online;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Packets.Handlers.Match;

public sealed class MatchHandlers
{
    private readonly RoomManager rooms;
    private readonly MatchScheduler matches;
    private readonly DeskManager desks;
    private readonly LobbyRoles lobbyRoles;
    private readonly FriendPresence presence;
    private readonly CreditManager credit;
    private readonly RankedManager ranked;
    private readonly TimeProvider clock;
    private readonly ILogger<MatchHandlers> logger;

    public MatchHandlers(
        RoomManager rooms,
        MatchScheduler matches,
        DeskManager desks,
        LobbyRoles lobbyRoles,
        FriendPresence presence,
        CreditManager credit,
        RankedManager ranked,
        TimeProvider clock,
        ILogger<MatchHandlers> logger)
    {
        this.rooms = rooms;
        this.matches = matches;
        this.desks = desks;
        this.lobbyRoles = lobbyRoles;
        this.presence = presence;
        this.credit = credit;
        this.ranked = ranked;
        this.clock = clock;
        this.logger = logger;
        matches.MatchConfirmed += OnMatchConfirmedAsync;
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomStartGameReq)]
    public async Task<GamePacket[]> OnRoomStartGameReq(GamePacket request)
    {
        pbCSRoomStartGameReq.Parser.ParseFrom(request.PkgData);
        if (await FindCreditShortfallAsync(request.Uid) is { } creditRefusal)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomStartGameRsp, new pbSCRoomStartGameRsp { ResultInfo = new pbResultInfo { ResultId = (int)creditRefusal } })];
        }

        if (rooms.FindRoomOf(request.Uid) is { } room)
        {
            await ranked.LoadAllAsync(room.Members.Select(member => member.Uid).ToArray());
        }

        var outcome = rooms.StartGame(request.Uid);
        logger.LogInformation("Uid {Uid} starts matching: {Result}", request.Uid, outcome.Result);
        if (!outcome.Succeeded)
        {
            return [request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomStartGameRsp, new pbSCRoomStartGameRsp { ResultInfo = Result(outcome) })];
        }

        var replies = await DeliverAsync(request, outcome);
        matches.Follow(outcome);
        if (outcome.StartedGame is not { } match)
        {
            return replies;
        }

        var deskOutcome = await BeginDeskAsync(match);
        return [.. replies, .. await DeliverAsync(request, deskOutcome)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidRoomConfirmReq)]
    public async Task<GamePacket[]> OnRoomConfirmReq(GamePacket request)
    {
        pbCSRoomConfirmReq.Parser.ParseFrom(request.PkgData);
        var outcome = rooms.ConfirmMatch(request.Uid);
        logger.LogInformation("Uid {Uid} confirms the match: {Result}", request.Uid, outcome.Result);
        if (!outcome.Succeeded)
        {
            return
            [
                request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidRoomConfirmRsp, new pbSCRoomConfirmRsp
                {
                    Uid = request.Uid,
                    FromGameEntity = MatchRoom.GameEntity,
                    ResultInfo = Result(outcome),
                }),
            ];
        }

        var confirmReplies = await DeliverAsync(request, outcome);
        if (outcome.StartedGame is not { } match)
        {
            return confirmReplies;
        }

        var deskOutcome = await BeginDeskAsync(match);
        return [.. confirmReplies, .. await DeliverAsync(request, deskOutcome)];
    }

    [PacketHandler(eNetEventServerType.LobbyServer, PB_ENUM_CS_MSGID_DEF.PbCsidCancelMatchReq)]
    public async Task<GamePacket[]> OnCancelMatchReq(GamePacket request)
    {
        var outcome = rooms.CancelMatch(request.Uid);
        logger.LogInformation("Uid {Uid} cancels matching: {Result}", request.Uid, outcome.Result);
        var response = request.Reply(PB_ENUM_CS_MSGID_DEF.PbScidCancelMatchRsp, new pbSCCancelMatchRsp { ResultInfo = Result(outcome) });
        return await DeliverAsync(request, outcome, response);
    }

    private async Task<PB_ENUM_RES_ERROR_CODE?> FindCreditShortfallAsync(ulong uid)
    {
        if (rooms.FindRoomOf(uid) is not { } room)
        {
            return null;
        }

        var (mapId, mapSubMode, memberUids) = (room.MapId, room.MapSubMode, room.Members.Select(member => member.Uid).OrderBy(memberUid => memberUid != uid).ToArray());
        var needCredit = credit.FindGameStartNeedCredit(mapId);
        foreach (var memberUid in memberUids)
        {
            var creditPoint = await credit.GetCreditPointAsync(memberUid);
            if (creditPoint < needCredit)
            {
                logger.LogInformation(
                    "Uid {Uid} cannot start map {MapId}: uid {MemberUid} has {CreditPoint} credit of the {NeedCredit} needed",
                    uid,
                    mapId,
                    memberUid,
                    creditPoint,
                    needCredit);
                var isRanked = mapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank;
                return (memberUid == uid, isRanked) switch
                {
                    (true, true) => PB_ENUM_RES_ERROR_CODE.PbErrBackToRankRoomCreditNotEnough,
                    (true, false) => PB_ENUM_RES_ERROR_CODE.PbErrBackToRoomCreditNotEnough,
                    (false, true) => PB_ENUM_RES_ERROR_CODE.PbErrChangeRankRoomCreditNotEnough,
                    (false, false) => PB_ENUM_RES_ERROR_CODE.PbErrChangeRoomCreditNotEnough,
                };
            }
        }

        return null;
    }

    private async Task OnMatchConfirmedAsync(PendingMatch match)
    {
        try
        {
            await PushAllAsync(await BeginDeskAsync(match));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Desk start for match {MatchID} failed", match.Ticket.MatchID);
        }
    }

    private async Task<RoomOutcome> BeginDeskAsync(PendingMatch match)
    {
        var deskOutcome = await desks.BeginDeskAsync(match);
        if (deskOutcome.Succeeded && match.LeadRoom.MapSubMode == (int)PB_ENUM_RES_MATCHMAPSUBMODE.PbMatchMapSubModeRaceRank)
        {
            await ranked.ConsumeTicketsAsync(match.HumanUids);
        }

        foreach (var uid in match.HumanUids)
        {
            await presence.SetGameStateAsync(
                uid,
                deskOutcome.Succeeded ? PB_ENUM_E_ACNT_GAMESTATE.PbEacntGamemulti : PB_ENUM_E_ACNT_GAMESTATE.PbEacntGameidle,
                deskOutcome.Succeeded ? clock.GetUtcNow().ToUnixTimeSeconds() : 0);
        }

        return deskOutcome;
    }

    private async Task<GamePacket[]> DeliverAsync(GamePacket request, RoomOutcome outcome, params GamePacket[] leading)
    {
        await PushAllAsync(outcome, except: request.Uid);
        var ownPushes = outcome.Pushes
            .Where(push => push.Uid == request.Uid)
            .Select(push => request.Reply(push.Message.MsgID, push.Message.Message))
            .ToArray();
        return
        [
            .. ownPushes.Take(outcome.RequesterPushesBeforeResponse),
            .. leading,
            .. outcome.Replies.Select(reply => request.Reply(reply.MsgID, reply.Message)),
            .. ownPushes.Skip(outcome.RequesterPushesBeforeResponse),
        ];
    }

    private async Task PushAllAsync(RoomOutcome outcome, ulong except = 0)
    {
        foreach (var group in outcome.Pushes.Where(push => push.Uid != except).GroupBy(push => push.Uid))
        {
            var packets = group.Select(push => GamePacket.Create(push.Message.MsgID, push.Message.Message)).ToArray();
            if (!await lobbyRoles.PushAsync(group.Key, packets))
            {
                logger.LogInformation("Match notices for uid {Uid} were not delivered: the role is offline", group.Key);
            }
        }
    }

    private static pbResultInfo Result(RoomOutcome outcome) => new() { ResultId = (int)outcome.Result, Args = outcome.ResultArgs };
}
