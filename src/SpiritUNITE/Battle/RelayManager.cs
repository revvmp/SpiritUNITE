using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Databin;
using SpiritUNITE.Game.Login;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;
using static SpiritUNITE.Packets.Messages.PB_ENUM_CS_FRAPBOOT_ACNTSTATECHG;

namespace SpiritUNITE.Battle;

public sealed class RelayManager
{
    private const int KFrapsLater = 1;
    private const int KFrapsFreqMs = 66;
    private const int PreActFrap = 20;
    private const int MaxRandomSeed = 10000;
    private const int HaskChkFreq = 60;
    private const int CltLogSize = 10 * 1024 * 1024;
    private const int NpcProcessBroadcast = 1;
    private const int NoNpcProcessBroadcast = 0;
    private const int SingleAcntOn = 1;
    private const int SingleAcntOff = 0;
    private const int MsgUseNoTag = 1;
    private const int BeginLoadClientSeq = 1;
    private const int BeginfightClientSeq = 2;
    private const int DefaultCanopyID = 1007500003;
    private const int OpenCCSyncMove = 1;
    private const int SlowUpMoveCmd = 1;
    private const int MaxCltCacheFrapNum = 900;
    private const int CltNeedWait = 1;
    private const int PauseType = (int)PB_ENUM_CS_PAUSE_TYPE.PbCsPauseCancel;
    private const int PauseCancelType = (int)PB_ENUM_COM_PAUSE_CANCEL_TYPE.PbComPauseCancelManual;
    private const int MinNpcLoadSeconds = 13;
    private const int MaxNpcLoadSeconds = 20;
    private const int FrapBootSession = 3;
    private const int LossFrapBootSession = 8;
    private const int FrapBootSessionStepFrames = 76;
    private const int FirstFrapBootSession = 0;
    private const int RedundantFrapNum = 4;
    private const int MaxSpareFrapNum = (int)PB_MACRO_CS_MAX_SPAREFRAP_NUM.PbCsMaxSparefrapNum;
    private const int AcntObjType = 1;
    private const int UsedTimeToleranceSeconds = 10;
    private const double MillisecondsPerSecond = 1000;
    private const int MaxUperInfoPerReq = 8;
    private const int MaxUperBuffBytes = 512;
    private const int MaxBootInfoPerObjPerFrame = 16;
    private const int MaxBootInfoBytesPerObjPerFrame = 1024;
    private const int MaxRepairRanges = 20;
    private const int MaxRepairFrames = (int)PB_MACRO_CS_MAX_REPAIR_FRAP_NUM.PbCsMaxRepairFrapNum;
    private const int MaxRepairBufBytes = 48 * 1024;
    private const int MaxReportedRelayIPLength = 64;
    private const int SecondGameoverRspVersion = 1;
    private const int HashKeptFrames = 35;
    private const int SelfObjID = 0;
    private const int FirstUnknownSyncType = (int)CmdInfoCase.StFrameCmdAddGainScore;
    private const int LastSyncType = 40;
    private const int Disconned2HangupTimeKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalDisconned2HangupTime;
    private const int Afk2HangupTimeKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalAfk2HangupTime;
    private const int Disconned2AiHostingTimeKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalDisconned2AiHostingTime;
    private const int Afk2AiHostingTimeKey = (int)PB_ENUM_RES_RESGLOBAL.PbResglobalAfk2AiHostingTime;
    private const int NotSurrender = 0;
    private const int SurrenderEnded = 1;
    private const int SurrenderEndSyncType = (int)CmdInfoCase.StFrameCmdSurrenderEnd;
    private const int HashNotEqual = 1;
    private const int HashEqual = 0;
    private const int NoLogUpload = 0;
    private const int VoteAgrees = 1;
    private const int DisabledSurrenderStartTime = 9999;

    private static readonly TimeSpan LoadTickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FirstLoadNoticeDelay = TimeSpan.FromMilliseconds(340);
    private static readonly TimeSpan MinFrameWait = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan AttachWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LoadGrace = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan SecondGameoverRspDelay = TimeSpan.FromMilliseconds(520);
    private static readonly TimeSpan SettlementDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FinishDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AbandonDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AttachTimeout = LoadTimeout;
    private static readonly TimeSpan StragglerNoticeDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StragglerTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan LostSilence = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DisconnSilence = TimeSpan.FromSeconds(37);
    private static readonly TimeSpan GameTimeMargin = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FallbackMaxGameDuration = TimeSpan.FromMinutes(30);

    private readonly DeskManager desks;
    private readonly BattleSettlement settlement;
    private readonly RelayRoles relayRoles;
    private readonly GameData gameData;
    private readonly TimeProvider clock;
    private readonly ILogger<RelayManager> logger;
    private readonly Lock gate = new();
    private readonly Dictionary<int, RelayGame> gamesByDesk = [];
    private readonly Dictionary<ulong, RelayGame> gamesByUid = [];

    public event Func<ulong, Task>? RelayFinished;

    public RelayManager(
        DeskManager desks,
        BattleSettlement settlement,
        RelayRoles relayRoles,
        GameData gameData,
        LoginManager login,
        TimeProvider clock,
        ILogger<RelayManager> logger)
    {
        this.desks = desks;
        this.settlement = settlement;
        this.relayRoles = relayRoles;
        this.gameData = gameData;
        this.clock = clock;
        this.logger = logger;
        relayRoles.RelayOnline += AttachAsync;
        relayRoles.RelayOffline += DetachAsync;
        desks.RelayHandedOff += desk => _ = ExpireUnattachedAsync(desk);
        login.ConnRelayRecover = BuildConnRelayRecover;
    }

    private async Task AttachAsync(ulong uid)
    {
        RelayGame? game = null;
        var created = false;
        lock (gate)
        {
            if (desks.FindRelayDesk(uid) is { } relayDesk && relayDesk.FindHuman(uid) is not null)
            {
                if (!gamesByDesk.TryGetValue(relayDesk.DeskID, out var existing) || existing.Desk != relayDesk)
                {
                    existing = CreateGame(relayDesk);
                    gamesByDesk[relayDesk.DeskID] = existing;
                    created = true;
                }

                game = existing;
                gamesByUid[uid] = game;
            }
        }

        if (game is null)
        {
            logger.LogWarning("Relay session of uid {Uid} has no desk in the relay phase", uid);
            relayRoles.Close(uid);
            return;
        }

        var sends = new List<RelaySend>();
        bool finished;
        lock (game.Gate)
        {
            finished = game.Finished;
            if (!finished)
            {
                Attach(game, game.Seats[uid], sends);
            }
        }

        if (finished)
        {
            relayRoles.Close(uid);
            return;
        }

        logger.LogInformation("Uid {Uid} joined relay desk {DeskID}", uid, game.Desk.DeskID);
        await SendAllAsync(sends);
        if (created)
        {
            _ = RunAsync(game);
        }
    }

    private void Attach(RelayGame game, RelaySeat seat, List<RelaySend> sends)
    {
        var now = clock.GetTimestamp();
        seat.IsConnected = true;
        seat.LastHeardAt = now;
        seat.ForgetCmdSeqs();
        game.AllLeftAt = null;
        switch (game.Phase)
        {
            case RelayGamePhase.Waiting when game.AllHumansAttached:
                BeginLoading(game, sends);
                break;
            case RelayGamePhase.Loading:
                var loadNotices = BuildCurrentLoadNotices(game, seat.Uid);
                sends.Add(new RelaySend(seat.Uid, RelayChannel.Stream, [.. seat.LoadFinished ? loadNotices : loadNotices.Prepend(BuildBeginLoadPacket(game))]));
                seat.HasBeginLoad = true;
                break;
            case RelayGamePhase.Fighting:
                seat.LastCommandAt = now;
                seat.DisconnInjected = false;
                if (game.RunsFrameLoop)
                {
                    MarkLoss(game, seat);
                    if (seat.Takeover == RelaySeatTakeover.AiHosted && !seat.QuitByExitButton)
                    {
                        InjectAcntState(game, seat, PbCsFrapbootAcntstateAiEnd);
                        seat.Takeover = RelaySeatTakeover.None;
                    }
                }

                break;
        }
    }

    private void BeginLoading(RelayGame game, List<RelaySend> sends)
    {
        game.Phase = RelayGamePhase.Loading;
        game.LoadStartedAt = clock.GetTimestamp();
        game.LoadStartTime = NextWholeSecond();
        game.TakeSvrPkgSeq();
        var beginLoad = BuildBeginLoadPacket(game);
        foreach (var seat in game.ConnectedSeats)
        {
            seat.HasBeginLoad = true;
            sends.Add(new RelaySend(seat.Uid, RelayChannel.Stream, [beginLoad]));
        }

        logger.LogInformation("Desk {DeskID} begins loading with {Connected} of {Humans} trainers on the relay", game.Desk.DeskID, game.ConnectedSeats.Count(), game.Seats.Count);
    }

    private async Task DetachAsync(ulong uid)
    {
        if (FindGame(uid) is not { } game)
        {
            return;
        }

        List<SeatCompletion> completions = [];
        bool finishes;
        lock (game.Gate)
        {
            if (game.FindSeat(uid) is not { IsConnected: true } seat)
            {
                return;
            }

            seat.IsConnected = false;
            if (!game.Finished && game.Phase == RelayGamePhase.Fighting)
            {
                seat.EverLost = true;
                if (game.FirstGameoverAt is not null && game.AllPlayingHumansReported)
                {
                    completions = EndFight(game);
                }
            }

            finishes = !game.Finished && game.Phase == RelayGamePhase.Over && game.AllHumansLeft && completions.Count == 0;
        }

        logger.LogInformation("Uid {Uid} left relay desk {DeskID}", uid, game.Desk.DeskID);
        StartCompletions(game, completions, finishesDesk: true);
        if (finishes)
        {
            await FinishAsync(game);
        }
    }

    public async Task LoadProcessAsync(ulong uid, pbCSMultgameLoadprocessReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } seat) || request.Process is < 0 or > RelayGame.FullProcess)
        {
            return;
        }

        ulong[] others;
        lock (game.Gate)
        {
            Heard(seat);
            if (game.Phase != RelayGamePhase.Loading)
            {
                return;
            }

            game.Processes[seat.ObjId] = request.Process;
            others = [.. game.ConnectedSeats.Where(other => other.Uid != uid && other.HasBeginLoad).Select(other => other.Uid)];
        }

        var notice = BuildLoadprocessNtf(seat.ObjId, request.Process);
        foreach (var other in others)
        {
            await relayRoles.PushAsync(other, RelayChannel.Stream, notice);
        }
    }

    public pbSCMultgameLoadfinRsp LoadFinished(ulong uid)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return new pbSCMultgameLoadfinRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        lock (game.Gate)
        {
            Heard(seat);
            if (game.Phase is RelayGamePhase.Waiting || !seat.HasBeginLoad)
            {
                return new pbSCMultgameLoadfinRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid) };
            }

            seat.LoadFinished = true;
            if (game.Phase == RelayGamePhase.Fighting)
            {
                logger.LogInformation("Desk {DeskID}: uid {Uid} finished loading during the fight at frame {KFrapsNo}", game.Desk.DeskID, uid, game.KFrapsNo);
            }

            return new pbSCMultgameLoadfinRsp { State = (int)game.ReconnStateOf(seat) };
        }
    }

    public pbSCReConnGameRsp ReConnGame(ulong uid)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return new pbSCReConnGameRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        lock (game.Gate)
        {
            Heard(seat);
            var state = game.ReconnStateOf(seat);
            return new pbSCReConnGameRsp
            {
                SelfObjID = seat.ObjId,
                State = (int)state,
                CampSurrenderInfo = game.Votes.GetValueOrDefault(seat.Human.CampClient) is { Kind: CampVoteKind.Surrender } vote ? vote.BuildCampSurrenderInfo() : new pbCOMDT_CAMP_SURRENDER_INFO(),
                AcntSurrenderInfo = new pbCOMDT_ACNT_SURRENDER_INFO(),
                VoiceAccusationInfo = new pbCOMDT_ACNT_VOICE_ACCUSATION_INFO(),
                IsSurrenderEnd = game.SurrenderWinCampClient is null ? NotSurrender : SurrenderEnded,
                WinCampClient = game.SurrenderWinCampClient ?? 0,
                PauseType = PauseType,
                CurFrapNo = state == PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateGameing ? game.KFrapsNo : 0,
                PauseCancelType = PauseCancelType,
            };
        }
    }

    public async Task FetchMsgFromVideoAsync(ulong uid, pbCSFetchMsgFromVideoReq fetch)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return;
        }

        GamePacket[] stream;
        lock (game.Gate)
        {
            Heard(seat);
            if (fetch.MsgId != (int)PB_ENUM_CS_MSGID_DEF.PbScidMultgameBeginLoadNtf || game.Phase is not (RelayGamePhase.Loading or RelayGamePhase.Fighting))
            {
                logger.LogInformation("Desk {DeskID}: uid {Uid} asked to replay message {MsgId} in phase {Phase}", game.Desk.DeskID, uid, fetch.MsgId, game.Phase);
                return;
            }

            seat.HasBeginLoad = true;
            seat.LoadFinished = false;
            if (game.Phase == RelayGamePhase.Fighting && game.RunsFrameLoop)
            {
                MarkLoss(game, seat);
            }

            logger.LogInformation("Desk {DeskID}: uid {Uid} rejoins in phase {Phase} at frame {KFrapsNo}", game.Desk.DeskID, uid, game.Phase, game.KFrapsNo);
            stream = [BuildBeginLoadPacket(game), .. game.Phase == RelayGamePhase.Loading ? BuildCurrentLoadNotices(game, uid) : []];
        }

        await relayRoles.PushAsync(uid, RelayChannel.Unreliable, GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFetchMsgFromVideoRsp, new pbSCFetchMsgFromVideoRsp { ResultInfo = new pbResultInfo() }));
        await relayRoles.PushAsync(uid, RelayChannel.Stream, stream);
    }

    public async Task PingAsync(ulong uid, GamePacket ping)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return;
        }

        bool echoes;
        lock (game.Gate)
        {
            Heard(seat);
            echoes = game.Phase == RelayGamePhase.Fighting;
        }

        if (echoes)
        {
            await relayRoles.PushAsync(uid, RelayChannel.Unreliable, ping with { Uid = 0, ClientSeq = 0 });
        }
    }

    public pbSCMultGameAliveStateRsp AliveState(ulong uid, pbCSMultGameAliveStateReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } seat) || game.Desk.FindPlayer(request.AcntObjID) is null)
        {
            return new pbSCMultGameAliveStateRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        lock (game.Gate)
        {
            Heard(seat);
        }

        logger.LogDebug("Uid {Uid} reports object {AcntObjID} alive state {AliveState}", uid, request.AcntObjID, request.AliveState);
        return new pbSCMultGameAliveStateRsp { ResultInfo = new pbResultInfo() };
    }

    public async Task HangUpAsync(ulong uid, pbCSOpeningGameHangUpReq request)
    {
        logger.LogInformation("Uid {Uid} reports opening hang-up of {HangUpAcntUid}", uid, request.HangUpAcntUid);
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return;
        }

        var sends = new List<RelaySend>();
        lock (game.Gate)
        {
            Heard(seat);
            if (game.HangUpReported || game.Phase != RelayGamePhase.Fighting || game.Finished || game.SurrenderWinCampClient is not null)
            {
                return;
            }

            var hungUp = request.HangUpAcntUid
                .Take(game.Desk.Players.Count)
                .Select(id => game.Desk.Players.FirstOrDefault(player => (ulong)player.ObjId == id || (!player.IsNpc && player.Uid == id)))
                .OfType<DeskPlayer>()
                .Distinct()
                .ToList();
            if (hungUp.Count == 0)
            {
                return;
            }

            game.HangUpReported = true;
            foreach (var camp in hungUp.Select(player => player.CampClient).Distinct().Order())
            {
                sends.AddRange(StartHangUpVote(game, camp, hungUp));
            }
        }

        await SendAllAsync(sends);
    }

    private List<RelaySend> StartHangUpVote(RelayGame game, int camp, List<DeskPlayer> hungUp)
    {
        var voters = game.Desk.CampOf(camp).Where(player => !hungUp.Contains(player) && !VotesAutomatically(game, player)).ToList();
        if (game.Limits.Surrender.InvalidSeconds <= 0 || game.Votes.ContainsKey(camp) || voters.Count == 0)
        {
            logger.LogInformation("Desk {DeskID}: no hang-up vote for camp {Camp}: no trainer of the camp can vote", game.Desk.DeskID, camp);
            return [];
        }

        var vote = new CampVote(CampVoteKind.HangUp, camp, voters, clock.GetTimestamp(), clock.GetUtcNow().ToUnixTimeSeconds());
        game.Votes[camp] = vote;
        logger.LogInformation("Desk {DeskID}: camp {Camp} votes on ending the game for the opening hang-up of objects {ObjIds}", game.Desk.DeskID, camp, hungUp.Where(player => player.CampClient == camp).Select(player => player.ObjId));
        return [.. CampNotices(game, camp, [BuildHangUpVoteInfoNtf(vote.BuildCampSurrenderInfo())])];
    }

    public async Task<pbSCHangUpGameOverVoteResultRsp> HangUpVoteAsync(ulong uid, pbCSHangUpGameOverVoteResultReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return new pbSCHangUpGameOverVoteResultRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        var sends = new List<RelaySend>();
        lock (game.Gate)
        {
            Heard(seat);
            var camp = seat.Human.CampClient;
            if (game.Phase != RelayGamePhase.Fighting || game.Finished || request.VoteResult is not (0 or VoteAgrees)
                || game.Votes.GetValueOrDefault(camp) is not { Kind: CampVoteKind.HangUp } vote || !vote.TryCast(seat.Human, request.VoteResult == VoteAgrees))
            {
                logger.LogInformation("Desk {DeskID}: refused the hang-up vote {VoteResult} of uid {Uid}", game.Desk.DeskID, request.VoteResult, uid);
                return new pbSCHangUpGameOverVoteResultRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid) };
            }

            logger.LogInformation("Desk {DeskID}: uid {Uid} votes {VoteResult} on ending the game; {Agrees} of {ValidCnt} agree", game.Desk.DeskID, uid, request.VoteResult, vote.Agrees, vote.SurrenderValidCnt);
            sends.AddRange(CampNotices(game, camp, [BuildHangUpVoteInfoNtf(vote.BuildCampSurrenderInfo())]));
            sends.AddRange(CloseDecidedVote(game, vote));
        }

        await SendAllAsync(sends);
        return new pbSCHangUpGameOverVoteResultRsp { ResultInfo = new pbResultInfo() };
    }

    private static IEnumerable<RelaySend> CampNotices(RelayGame game, int camp, GamePacket[] notices) =>
        game.ConnectedSeats
            .Where(seat => seat.Human.CampClient == camp)
            .Select(seat => new RelaySend(seat.Uid, RelayChannel.Stream, notices));

    private List<RelaySend> ExpireVotes(RelayGame game)
    {
        var sends = new List<RelaySend>();
        var invalidAfter = TimeSpan.FromSeconds(game.Limits.Surrender.InvalidSeconds);
        foreach (var vote in game.Votes.Values.Where(vote => clock.GetElapsedTime(vote.StartedAt) >= invalidAfter).ToList())
        {
            game.Votes.Remove(vote.CampClient);
            logger.LogInformation("Desk {DeskID}: the {Kind} vote of camp {Camp} expired with {Agrees} of {ValidCnt}", game.Desk.DeskID, vote.Kind, vote.CampClient, vote.Agrees, vote.SurrenderValidCnt);
            var info = vote.BuildExpiredCampSurrenderInfo();
            sends.AddRange(CampNotices(game, vote.CampClient, [vote.Kind == CampVoteKind.Surrender
                ? GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidSurrenderNtf, new pbSCSurrenderNtf { CampSurrenderInfo = info })
                : BuildHangUpVoteInfoNtf(info)]));
        }

        return sends;
    }

    public async Task HashCheckAsync(ulong uid, pbCSRelayHashcheckReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return;
        }

        RelaySend[] sends = [];
        lock (game.Gate)
        {
            Heard(seat);
            if (game.Phase != RelayGamePhase.Fighting || !game.RunsFrameLoop || request.KFrapsNo <= 0 || request.KFrapsNo > game.KFrapsNo)
            {
                return;
            }

            if (game.RecordHash(request.KFrapsNo, uid, request.HashToChk, HashKeptFrames) is not { } hashes)
            {
                return;
            }

            var deskHash = FindDeskHash(hashes);
            var deskEqual = hashes.Values.Distinct().Count() == 1;
            if (!deskEqual)
            {
                logger.LogWarning(
                    "Desk {DeskID}: the trainers' hashes differ at frame {KFrapsNo}; out of sync: {Uids}",
                    game.Desk.DeskID,
                    request.KFrapsNo,
                    hashes.Where(entry => deskHash is null || entry.Value != deskHash).Select(entry => entry.Key));
            }

            sends = [.. game.ConnectedSeats.Select(connected => new RelaySend(
                connected.Uid,
                RelayChannel.Stream,
                [GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbCsidRelayHashcheckRsp, BuildHashcheckRsp(request.KFrapsNo, connected, hashes, deskEqual, deskHash))]))];
        }

        await SendAllAsync(sends);
    }

    private static long? FindDeskHash(IReadOnlyDictionary<ulong, long> hashes)
    {
        var counts = hashes.Values.CountBy(hash => hash).OrderByDescending(entry => entry.Value).ToList();
        return counts.Count == 1 || counts[0].Value > counts[1].Value ? counts[0].Key : null;
    }

    private static pbCSRelayHashcheckRsp BuildHashcheckRsp(int kFrapsNo, RelaySeat seat, IReadOnlyDictionary<ulong, long> hashes, bool deskEqual, long? deskHash)
    {
        var rsp = new pbCSRelayHashcheckRsp { KFrapsNo = kFrapsNo, AcntObjID = seat.ObjId };
        if (!deskEqual)
        {
            var selfEqual = deskHash is { } agreed && hashes.TryGetValue(seat.Uid, out var own) && own == agreed;
            rsp.IsSelfNE = selfEqual ? HashEqual : HashNotEqual;
            rsp.IsDeskNE = HashNotEqual;
            rsp.NeedUploadLog = NoLogUpload;
        }

        return rsp;
    }

    public async Task<GamePacket[]> SurrenderAsync(ulong uid, pbCSSurrenderReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return [BuildSurrenderRsp(Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk), 0)];
        }

        var sends = new List<RelaySend>();
        GamePacket[] toSender;
        lock (game.Gate)
        {
            Heard(seat);
            var camp = seat.Human.CampClient;
            if (FindSurrenderFault(game, seat, request) is { } fault)
            {
                logger.LogInformation("Desk {DeskID}: refused the surrender vote of uid {Uid} (flag {SurrenderFlag}): {Fault}", game.Desk.DeskID, uid, request.SurrenderFlag, fault);
                return [BuildSurrenderRsp(Refusal(PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid), 0)];
            }

            if (request.SurrenderCampIdx != camp)
            {
                logger.LogDebug("Desk {DeskID}: uid {Uid} named camp {SurrenderCampIdx} in its surrender vote; its camp is {Camp}", game.Desk.DeskID, uid, request.SurrenderCampIdx, camp);
            }

            var vote = game.Votes.GetValueOrDefault(camp) ?? StartSurrenderVote(game, seat);
            vote.TryCast(seat.Human, request.SurrenderFlag == VoteAgrees);
            var notices = new List<GamePacket> { BuildSurrenderNtf(vote) };
            notices.AddRange(CastAutomaticVotes(game, vote, BuildSurrenderNtf));
            logger.LogInformation(
                "Desk {DeskID}: uid {Uid} votes {Agrees} on the surrender of camp {Camp}; {AgreesCnt} of {ValidCnt} agree, {Needed} needed",
                game.Desk.DeskID,
                uid,
                request.SurrenderFlag == VoteAgrees ? "yes" : "no",
                camp,
                vote.Agrees,
                vote.SurrenderValidCnt,
                vote.AgreesNeeded);
            toSender = [BuildSurrenderRsp(new pbResultInfo(), vote.SurrenderTime), .. notices];
            sends.AddRange(game.ConnectedSeats
                .Where(other => other.Uid != uid && other.Human.CampClient == camp)
                .Select(other => new RelaySend(other.Uid, RelayChannel.Stream, [.. notices])));
            sends.AddRange(CloseDecidedVote(game, vote));
        }

        await SendAllAsync(sends);
        return toSender;
    }

    private string? FindSurrenderFault(RelayGame game, RelaySeat seat, pbCSSurrenderReq request)
    {
        if (game.Phase != RelayGamePhase.Fighting || game.Finished || game.SurrenderWinCampClient is not null)
        {
            return $"the desk is in phase {game.Phase}";
        }

        if (request.SurrenderFlag is not (0 or VoteAgrees))
        {
            return "the surrender flag is invalid";
        }

        var camp = seat.Human.CampClient;
        if (game.Votes.GetValueOrDefault(camp) is { } open)
        {
            return open.Kind != CampVoteKind.Surrender ? "a hang-up vote is open"
                : !open.CanVote(seat.Human) ? "the trainer cannot vote or already voted"
                : null;
        }

        if (request.SurrenderFlag != VoteAgrees)
        {
            return "no vote is open";
        }

        var rules = game.Limits.Surrender;
        var fightSeconds = clock.GetElapsedTime(game.FightStartedAt).TotalSeconds;
        return fightSeconds < rules.StartSeconds ? $"surrender opens at {rules.StartSeconds} s, the fight is at {fightSeconds:F0} s"
            : seat.StartedSurrenderVotes >= rules.TimesLimit ? $"the trainer already started {seat.StartedSurrenderVotes} votes"
            : game.LastSurrenderTimes.TryGetValue(camp, out var last) && clock.GetUtcNow().ToUnixTimeSeconds() < last + rules.CDSeconds ? "the camp's surrender is in its cool down"
            : null;
    }

    private CampVote StartSurrenderVote(RelayGame game, RelaySeat initiator)
    {
        var camp = initiator.Human.CampClient;
        var vote = new CampVote(CampVoteKind.Surrender, camp, [.. game.Desk.CampOf(camp)], clock.GetTimestamp(), clock.GetUtcNow().ToUnixTimeSeconds());
        initiator.StartedSurrenderVotes++;
        game.LastSurrenderTimes[camp] = vote.SurrenderTime;
        game.Votes[camp] = vote;
        return vote;
    }

    private static List<GamePacket> CastAutomaticVotes(RelayGame game, CampVote vote, Func<CampVote, GamePacket> buildNotice)
    {
        var notices = new List<GamePacket>();
        foreach (var voter in vote.Voters.Where(voter => VotesAutomatically(game, voter)))
        {
            if (vote.TryCast(voter, agrees: true))
            {
                notices.Add(buildNotice(vote));
            }
        }

        return notices;
    }

    private static bool VotesAutomatically(RelayGame game, DeskPlayer voter) =>
        voter.IsNpc || game.FindSeat(voter.Uid) is { IsConnected: false };

    private List<RelaySend> CloseDecidedVote(RelayGame game, CampVote vote)
    {
        if (vote.Outcome == CampVoteOutcome.Open)
        {
            return [];
        }

        game.Votes.Remove(vote.CampClient);
        if (vote.Outcome == CampVoteOutcome.Failed)
        {
            logger.LogInformation("Desk {DeskID}: the {Kind} vote of camp {Camp} failed with {Agrees} of {ValidCnt}", game.Desk.DeskID, vote.Kind, vote.CampClient, vote.Agrees, vote.SurrenderValidCnt);
            return [];
        }

        return EndBySurrender(game, vote);
    }

    private List<RelaySend> EndBySurrender(RelayGame game, CampVote vote)
    {
        var winCampClient = game.EnemyCampOf(vote.CampClient) ?? 0;
        game.SurrenderWinCampClient = winCampClient;
        game.Votes.Clear();
        logger.LogInformation(
            "Desk {DeskID}: camp {Camp} passed a {Kind} vote with {Agrees} of {ValidCnt}; camp {WinCampClient} wins at frame {KFrapsNo}",
            game.Desk.DeskID,
            vote.CampClient,
            vote.Kind,
            vote.Agrees,
            vote.SurrenderValidCnt,
            winCampClient,
            game.KFrapsNo + KFrapsLater);
        if (vote.Kind == CampVoteKind.Surrender && game.RunsFrameLoop)
        {
            InjectSurrenderEnd(game, vote.Initiator?.ObjId ?? SelfObjID, winCampClient);
            return [];
        }

        var end = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidHangUpVoteToEndGameNtf, new pbSCHangUpVoteToEndGameNtf { WinCampClient = winCampClient });
        return [.. game.ConnectedSeats.Select(seat => new RelaySend(seat.Uid, RelayChannel.Stream, [end]))];
    }

    private static GamePacket BuildSurrenderRsp(pbResultInfo resultInfo, long surrenderTime) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidSurrenderRsp, new pbSCSurrenderRsp { ResultInfo = resultInfo, SurrenderTime = surrenderTime });

    private static GamePacket BuildSurrenderNtf(CampVote vote) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidSurrenderNtf, new pbSCSurrenderNtf { CampSurrenderInfo = vote.BuildCampSurrenderInfo() });

    private static GamePacket BuildHangUpVoteInfoNtf(pbCOMDT_CAMP_SURRENDER_INFO info) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidHangUpGameOverVoteInfoNtf, new pbSCHangUpGameOverVoteInfoNtf { CampSurrenderInfo = info });

    public void ReportConnectionProblem(ulong uid, pbCSRelayConnectionProblemReport report)
    {
        if (!IsPrintableHost(report.RelayIP))
        {
            logger.LogWarning("Uid {Uid} reported a relay connection problem with an invalid {Length}-character relay address", uid, report.RelayIP.Length);
            return;
        }

        logger.LogWarning(
            "Uid {Uid} could not keep its relay connection to {RelayIP}:{RelayPort} (reason {Reason}); its desk is {DeskID}",
            uid,
            report.RelayIP,
            report.RelayPort,
            report.Reason,
            desks.FindRelayDesk(uid)?.DeskID);
    }

    public void Upermsg(ulong uid, ReadOnlyMemory<byte> pkgData)
    {
        if (FindSeat(uid) is not ({ } game, { } seat) || !game.RunsFrameLoop)
        {
            return;
        }

        pbCSGamingUpermsgReq request;
        try
        {
            request = NoTagPB.ParseGamingUpermsgReq(pkgData.Span, MaxUperInfoPerReq);
        }
        catch (InvalidDataException exception)
        {
            logger.LogDebug("Desk {DeskID}: dropped a malformed command upload of uid {Uid}: {Reason}", game.Desk.DeskID, uid, exception.Message);
            return;
        }

        lock (game.Gate)
        {
            Heard(seat);
            if (game.Phase != RelayGamePhase.Fighting || game.Finished)
            {
                return;
            }

            var dropped = 0;
            foreach (var uper in request.UperInfo)
            {
                if (!IsRelayableUper(game, uper, seat) || !seat.TryAcceptCmdSeq(uper.CmdSeq))
                {
                    dropped++;
                    continue;
                }

                if (!TryQueueCommand(game, seat, uper))
                {
                    dropped++;
                }
            }

            if (dropped > 0)
            {
                logger.LogTrace("Desk {DeskID}: dropped {Dropped} commands of uid {Uid}", game.Desk.DeskID, dropped, uid);
            }
        }
    }

    private static bool IsRelayableUper(RelayGame game, pbCSDT_GAMING_SYNC_INFO uper, RelaySeat seat) =>
        (uper.ObjID == SelfObjID || uper.ObjID == seat.ObjId)
        && uper.SyncType is >= 0 and <= LastSyncType and not FirstUnknownSyncType and not (int)CmdInfoCase.StAcntState
        && uper.Buff.Length <= MaxUperBuffBytes
        && (uper.SyncType != SurrenderEndSyncType || EndsAsSurrendered(game, uper));

    private static bool EndsAsSurrendered(RelayGame game, pbCSDT_GAMING_SYNC_INFO uper)
    {
        try
        {
            return game.SurrenderWinCampClient is { } winCampClient && NoTagPB.ReadFrameCmdPkgIntParam1(uper.Buff.Span) == winCampClient;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private bool TryQueueCommand(RelayGame game, RelaySeat seat, pbCSDT_GAMING_SYNC_INFO uper)
    {
        var objId = seat.ObjId;
        var existing = game.PendingBootInfo.FindIndex(command => command.ObjID == objId && command.SyncType == uper.SyncType && !IsInjected(command));
        var replacedBytes = existing >= 0 ? game.PendingBootInfo[existing].Buff.Length : 0;
        var objCommands = game.PendingBootInfo.Count(command => command.ObjID == objId && !IsInjected(command));
        if ((existing < 0 && objCommands >= MaxBootInfoPerObjPerFrame)
            || game.PendingBytesOf(objId) - replacedBytes + uper.Buff.Length > MaxBootInfoBytesPerObjPerFrame
            || game.PendingBytes - replacedBytes + uper.Buff.Length > RelayGame.MaxFrameInfoBytes)
        {
            return false;
        }

        seat.LastCommandAt = clock.GetTimestamp();
        if (!seat.QuitByExitButton)
        {
            if (seat.Takeover == RelaySeatTakeover.Hangup)
            {
                InjectAcntState(game, seat, PbCsFrapbootAcntstateRecovery);
                seat.Takeover = RelaySeatTakeover.None;
            }
            else if (seat.Takeover == RelaySeatTakeover.AiHosted)
            {
                InjectAcntState(game, seat, PbCsFrapbootAcntstateAiEnd);
                seat.Takeover = RelaySeatTakeover.None;
            }
        }

        var command = new pbCSDT_FRAPBOOT_CC { ObjID = objId, SyncType = uper.SyncType, Buff = uper.Buff };
        if (existing >= 0)
        {
            game.PendingBootInfo[existing] = command;
        }
        else
        {
            game.PendingBootInfo.Add(command);
        }

        return true;
    }

    private static bool IsInjected(pbCSDT_FRAPBOOT_CC command) => command.SyncType is (int)CmdInfoCase.StAcntState or SurrenderEndSyncType;

    private void InjectAcntState(RelayGame game, RelaySeat seat, PB_ENUM_CS_FRAPBOOT_ACNTSTATECHG state)
    {
        game.PendingBootInfo.Add(new pbCSDT_FRAPBOOT_CC
        {
            ObjID = seat.ObjId,
            SyncType = (int)CmdInfoCase.StAcntState,
            Buff = ByteString.CopyFrom(new FrameCmdPkg { Type = CmdInfoCase.StAcntState, IntParam1 = (int)state }.ToNoTagByteArray()),
        });
        logger.LogInformation("Desk {DeskID}: object {ObjID} of uid {Uid} becomes {State} at frame {KFrapsNo}", game.Desk.DeskID, seat.ObjId, seat.Uid, state, game.KFrapsNo + KFrapsLater);
    }

    public pbCSFrapBootRepairRsp RepairFrames(ulong uid, pbCSFrapBootRepairReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return new pbCSFrapBootRepairRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        lock (game.Gate)
        {
            Heard(seat);
            if (game.Phase == RelayGamePhase.Fighting && game.RunsFrameLoop && request.KFrapsNo.Count > 0)
            {
                MarkLoss(game, seat);
            }

            using var buf = new MemoryStream();
            foreach (var kFrapsNo in FindRepairFrames(game, request))
            {
                var stored = game.FindStoredFrame(kFrapsNo)!;
                var ntf = new pbSCFrapBootInfoNtf { Session = stored.Session, SpareFrap = { game.FindFrame(kFrapsNo)! } };
                var packet = BuildFrapBootInfoPacket(ntf, stored.SvrPkgSeq).ToRelayByteArray();
                if (buf.Length + packet.Length > MaxRepairBufBytes)
                {
                    break;
                }

                buf.Write(packet);
            }

            return new pbCSFrapBootRepairRsp
            {
                ResultInfo = new pbResultInfo(),
                MaxFrapNo = game.KFrapsNo,
                Buf = ByteString.CopyFrom(buf.ToArray()),
            };
        }
    }

    private static List<int> FindRepairFrames(RelayGame game, pbCSFrapBootRepairReq request)
    {
        var seen = new HashSet<int>();
        var frames = new List<int>();
        foreach (var range in request.KFrapsNo.Take(MaxRepairRanges))
        {
            var first = Math.Max(range.KFrapsNo, 1);
            var last = (int)Math.Min((long)range.KFrapsNo + Math.Clamp(range.KFrapsNum, 0, MaxRepairFrames) - 1, game.KFrapsNo);
            for (var kFrapsNo = first; kFrapsNo <= last && frames.Count < MaxRepairFrames; kFrapsNo++)
            {
                if (seen.Add(kFrapsNo))
                {
                    frames.Add(kFrapsNo);
                }
            }
        }

        return frames;
    }

    public pbSCMultgameGameoverRsp Gameover(ulong uid, pbCSMultgameGameoverReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } seat))
        {
            return new pbSCMultgameGameoverRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        List<SeatCompletion> completions;
        var finishesDesk = true;
        lock (game.Gate)
        {
            Heard(seat);
            if (seat.Gameover is not null)
            {
                return new pbSCMultgameGameoverRsp { ResultInfo = new pbResultInfo(), IsCltNeedWait = game.Phase == RelayGamePhase.Over ? 0 : CltNeedWait };
            }

            if (game.Phase is RelayGamePhase.Over)
            {
                logger.LogInformation("Desk {DeskID}: uid {Uid} reported the game over after the fight ended", game.Desk.DeskID, uid);
                return new pbSCMultgameGameoverRsp { ResultInfo = new pbResultInfo() };
            }

            if (game.Phase != RelayGamePhase.Fighting || game.Finished)
            {
                logger.LogWarning("Desk {DeskID}: uid {Uid} sent a game over in phase {Phase}", game.Desk.DeskID, uid, game.Phase);
                return new pbSCMultgameGameoverRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid) };
            }

            var fightSeconds = clock.GetElapsedTime(game.FightStartedAt).TotalSeconds;
            if (FindGameoverFault(game, seat.Human, request, fightSeconds) is { } fault)
            {
                logger.LogWarning("Desk {DeskID}: refused the game over of uid {Uid}: {Fault}", game.Desk.DeskID, uid, fault.Reason);
                return new pbSCMultgameGameoverRsp { ResultInfo = Refusal(fault.Code) };
            }

            seat.Gameover = request;
            seat.GameoverFightSeconds = (int)Math.Round(fightSeconds);
            seat.QuitByExitButton = request.IsExitButtonGameOver != 0;
            game.GameoverOrder.Add(uid);
            if (game.FindAgreedGameover() is { } agreed && !seat.QuitByExitButton && agreed.BattleParam.WinCampClient != request.BattleParam.WinCampClient)
            {
                logger.LogWarning(
                    "Desk {DeskID}: uid {Uid} reports winner camp {WinCampClient}, but the agreed winner is camp {AgreedWinCampClient}",
                    game.Desk.DeskID,
                    uid,
                    request.BattleParam.WinCampClient,
                    agreed.BattleParam.WinCampClient);
            }

            if (game.AllPlayingHumansReported)
            {
                completions = EndFight(game);
            }
            else if (seat.QuitByExitButton)
            {
                completions = [CompleteQuitter(game, seat)];
                finishesDesk = false;
            }
            else
            {
                game.FirstGameoverAt ??= clock.GetTimestamp();
                completions = [];
            }
        }

        logger.LogInformation(
            "Desk {DeskID}: uid {Uid} reports game over after {GameTime} s, winner camp {WinCampClient}{Quit}",
            game.Desk.DeskID,
            uid,
            seat.GameoverFightSeconds,
            request.BattleParam.WinCampClient,
            seat.QuitByExitButton ? " (quit button)" : string.Empty);
        StartCompletions(game, completions, finishesDesk);
        return new pbSCMultgameGameoverRsp { ResultInfo = new pbResultInfo(), IsCltNeedWait = CltNeedWait };
    }

    private SeatCompletion CompleteQuitter(RelayGame game, RelaySeat seat)
    {
        seat.Completed = true;
        if (game.RunsFrameLoop && seat.Takeover != RelaySeatTakeover.AiHosted)
        {
            InjectAcntState(game, seat, PbCsFrapbootAcntstateAiStart);
            seat.Takeover = RelaySeatTakeover.AiHosted;
        }

        return new SeatCompletion(new BattleResult(game.Desk, seat.Human, seat.Gameover!.Clone(), game.GameStartTime, seat.GameoverFightSeconds), SendsFinalRsp: true);
    }

    private List<SeatCompletion> EndFight(RelayGame game)
    {
        game.Phase = RelayGamePhase.Over;
        var agreed = game.FindAgreedGameover();
        var fightSeconds = (int)Math.Round(clock.GetElapsedTime(game.FightStartedAt).TotalSeconds);
        var completions = new List<SeatCompletion>();
        foreach (var seat in game.Seats.Values.Where(seat => !seat.Completed))
        {
            if (seat.Gameover is { } report)
            {
                var gameover = report.Clone();
                if (!seat.QuitByExitButton && agreed is not null)
                {
                    gameover.BattleParam = agreed.BattleParam.Clone();
                    gameover.GameoverSummary = agreed.GameoverSummary?.Clone();
                }

                completions.Add(new SeatCompletion(new BattleResult(game.Desk, seat.Human, gameover, game.GameStartTime, seat.GameoverFightSeconds), SendsFinalRsp: true));
                seat.Completed = true;
            }
            else if (seat.IsConnected && agreed is not null)
            {
                logger.LogInformation("Desk {DeskID}: uid {Uid} never reported the game over and is settled with the agreed result", game.Desk.DeskID, seat.Uid);
                completions.Add(new SeatCompletion(new BattleResult(game.Desk, seat.Human, agreed.Clone(), game.GameStartTime, fightSeconds), SendsFinalRsp: false));
                seat.Completed = true;
            }
        }

        logger.LogInformation(
            "Desk {DeskID}: the fight ended at frame {KFrapsNo} with winner camp {WinCampClient}; {Reporters} of {Humans} trainers reported",
            game.Desk.DeskID,
            game.KFrapsNo,
            agreed?.BattleParam.WinCampClient,
            game.GameoverOrder.Count,
            game.Seats.Count);
        return completions;
    }

    private static ulong[] EndTimedOutFight(RelayGame game)
    {
        game.Phase = RelayGamePhase.Over;
        var timedOut = game.Seats.Values.Where(seat => !seat.Completed).ToArray();
        foreach (var seat in timedOut)
        {
            seat.Completed = true;
        }

        return [.. timedOut.Select(seat => seat.Uid)];
    }

    private GameoverFault? FindGameoverFault(RelayGame game, DeskPlayer human, pbCSMultgameGameoverReq request, double fightSeconds)
    {
        var desk = game.Desk;
        var param = request.BattleParam;
        if (param is null || param.MapID != desk.DeskInfo.MapId || param.GameType != desk.DeskInfo.GameType)
        {
            return new GameoverFault(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, "the battle parameters do not match the desk");
        }

        var quitByExitButton = request.IsExitButtonGameOver != 0;
        if (!quitByExitButton && !BattleResult.IsDrawReport(param) && !desk.Players.Any(player => player.CampClient == param.WinCampClient))
        {
            return new GameoverFault(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, $"winner camp {param.WinCampClient} is not on the desk");
        }

        if (!request.AcntInfo.Any(info => info.AcntUid == human.Uid && info.ObjType == AcntObjType && info.AcntObjID == human.ObjId))
        {
            return new GameoverFault(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, "the sender's own entry is missing");
        }

        var endsBeforeFloor = !quitByExitButton && game.SurrenderWinCampClient is null && fightSeconds < game.Limits.MinGameSeconds;
        if (endsBeforeFloor || param.UsedTime / MillisecondsPerSecond > fightSeconds + UsedTimeToleranceSeconds)
        {
            return new GameoverFault(
                PB_ENUM_RES_ERROR_CODE.PbErrJudgeIllegalOperation,
                $"used time {param.UsedTime} ms does not fit {fightSeconds:F0} s of fighting");
        }

        return null;
    }

    private void StartCompletions(RelayGame game, List<SeatCompletion> completions, bool finishesDesk)
    {
        if (completions.Count > 0)
        {
            _ = CompleteGameoverAsync(game, completions, finishesDesk);
        }
    }

    private async Task CompleteGameoverAsync(RelayGame game, List<SeatCompletion> completions, bool finishesDesk)
    {
        try
        {
            await Task.Delay(SecondGameoverRspDelay, clock);
            var finalRsp = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultgameGameoverRsp, new pbSCMultgameGameoverRsp()) with
            {
                Version = SecondGameoverRspVersion,
            };
            foreach (var completion in completions.Where(completion => completion.SendsFinalRsp))
            {
                await relayRoles.PushAsync(completion.Result.Human.Uid, RelayChannel.Stream, finalRsp);
            }

            await Task.Delay(SettlementDelay, clock);
            foreach (var completion in completions)
            {
                try
                {
                    await settlement.SettleAsync(completion.Result);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Settlement of uid {Uid} on desk {DeskID} failed", completion.Result.Human.Uid, game.Desk.DeskID);
                }
            }

            if (!finishesDesk)
            {
                return;
            }

            await Task.Delay(FinishDelay, clock);
            await FinishAsync(game);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Game over on desk {DeskID} failed", game.Desk.DeskID);
        }
    }

    private async Task RunAsync(RelayGame game)
    {
        try
        {
            while (true)
            {
                var step = Advance(game);
                await SendAllAsync(step.Sends);
                StartCompletions(game, step.Completions, finishesDesk: true);
                foreach (var uid in step.TimedOutUids ?? [])
                {
                    await settlement.EndTimedOutGameAsync(game.Desk, uid);
                }

                if (step.Finishes)
                {
                    await FinishAsync(game);
                    return;
                }

                if (step.Wait is not { } wait)
                {
                    return;
                }

                await Task.Delay(wait, clock, game.StoppingToken);
            }
        }
        catch (OperationCanceledException) when (game.StoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("Desk {DeskID} relay loop stopped", game.Desk.DeskID);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Relay loop of desk {DeskID} failed", game.Desk.DeskID);
            await FinishAsync(game);
        }
    }

    private RelayStep Advance(RelayGame game)
    {
        lock (game.Gate)
        {
            if (game.Finished || game.Phase == RelayGamePhase.Over)
            {
                return new RelayStep([], [], Finishes: false, Wait: null);
            }

            var now = clock.GetTimestamp();
            if (IsAbandoned(game, now))
            {
                logger.LogInformation("Desk {DeskID} was abandoned: no trainer has been on the relay for {AbandonDelay}", game.Desk.DeskID, AbandonDelay);
                return new RelayStep([], [], Finishes: true, Wait: null);
            }

            var sends = new List<RelaySend>();
            switch (game.Phase)
            {
                case RelayGamePhase.Waiting:
                    if (clock.GetElapsedTime(game.CreatedAt) >= AttachWait && game.ConnectedSeats.Any())
                    {
                        logger.LogWarning("Desk {DeskID}: not every trainer reached the relay within {AttachWait}; loading starts without them", game.Desk.DeskID, AttachWait);
                        BeginLoading(game, sends);
                    }

                    return new RelayStep([.. sends], [], Finishes: false, LoadTickInterval);
                case RelayGamePhase.Loading:
                    return AdvanceLoad(game, sends);
                default:
                    return AdvanceFight(game, now, sends);
            }
        }
    }

    private bool IsAbandoned(RelayGame game, long now)
    {
        if (!game.AllHumansLeft)
        {
            game.AllLeftAt = null;
            return false;
        }

        game.AllLeftAt ??= now;
        return clock.GetElapsedTime(game.AllLeftAt.Value) >= AbandonDelay;
    }

    private RelayStep AdvanceLoad(RelayGame game, List<RelaySend> sends)
    {
        var loadElapsed = clock.GetElapsedTime(game.LoadStartedAt);
        var noticeElapsed = loadElapsed - FirstLoadNoticeDelay;
        if (noticeElapsed < TimeSpan.Zero)
        {
            return new RelayStep([], [], Finishes: false, -noticeElapsed);
        }

        var tick = (int)(noticeElapsed / LoadTickInterval);
        var nextTick = LoadTickInterval * (tick + 1) - noticeElapsed;
        if (tick <= game.LoadTick)
        {
            return new RelayStep([], [], Finishes: false, nextTick);
        }

        var firstNotice = game.LoadTick < 0;

        game.LoadTick = tick;
        foreach (var objId in game.NpcLoadSeconds.Keys)
        {
            game.Processes[objId] = game.NpcProcessAt(objId, game.LoadTick);
        }

        var humansReady = game.AllHumansLoaded || (game.AnyHumanLoaded && loadElapsed >= LoadGrace);
        var beginsFight = game.AllNpcsLoaded && humansReady;
        var notices = BroadcastNpcObjIds(game)
            .Where(_ => beginsFight || firstNotice || Random.Shared.Next(2) == 0)
            .Order()
            .Select(objId => BuildLoadprocessNtf(objId, game.Processes[objId]))
            .ToList();
        var loadingUids = game.ConnectedSeats.Where(seat => seat.HasBeginLoad).Select(seat => seat.Uid).ToList();
        if (!beginsFight)
        {
            if (loadElapsed > LoadTimeout)
            {
                logger.LogWarning("Desk {DeskID}: no trainer finished loading in {Timeout}", game.Desk.DeskID, LoadTimeout);
                return new RelayStep([], [], Finishes: true, Wait: null);
            }

            sends.AddRange(loadingUids.Select(uid => new RelaySend(uid, RelayChannel.Stream, [.. notices])));
            return new RelayStep([.. sends], [], Finishes: false, nextTick);
        }

        BeginFight(game);
        var beginfight = BuildBeginfightPacket(game);
        foreach (var uid in loadingUids)
        {
            var seat = game.Seats[uid];
            sends.Add(new RelaySend(uid, RelayChannel.Stream, [.. notices, .. seat.LoadFinished ? new[] { beginfight } : []]));
            seat.HasBeginfight = seat.LoadFinished;
        }

        logger.LogInformation("Desk {DeskID} begins the fight", game.Desk.DeskID);
        return new RelayStep([.. sends], [], Finishes: false, game.RunsFrameLoop ? MinFrameWait : LoadTickInterval);
    }

    private void BeginFight(RelayGame game)
    {
        game.Phase = RelayGamePhase.Fighting;
        game.GameStartTime = NextWholeSecond();
        game.FightStartedAt = clock.GetTimestamp();
        game.TakeSvrPkgSeq();
        game.FrapBootSession = FrapBootSession;
        foreach (var seat in game.Seats.Values)
        {
            seat.LastCommandAt = game.FightStartedAt;
            if (seat.IsConnected)
            {
                seat.LastHeardAt = game.FightStartedAt;
            }

            if (game.RunsFrameLoop && !seat.LoadFinished)
            {
                logger.LogWarning("Desk {DeskID}: uid {Uid} did not finish loading in time; an AI takes its seat", game.Desk.DeskID, seat.Uid);
                seat.EverLost = true;
                if (!seat.IsConnected)
                {
                    InjectAcntState(game, seat, PbCsFrapbootAcntstateDisconn);
                    seat.DisconnInjected = true;
                }

                InjectAcntState(game, seat, PbCsFrapbootAcntstateAiStart);
                seat.Takeover = RelaySeatTakeover.AiHosted;
            }
        }
    }

    private RelayStep AdvanceFight(RelayGame game, long now, List<RelaySend> sends)
    {
        var completions = new List<SeatCompletion>();
        var fightElapsed = clock.GetElapsedTime(game.FightStartedAt);
        if (fightElapsed >= game.Limits.MaxGameDuration)
        {
            logger.LogWarning("Desk {DeskID} reached the game time limit of {MaxGameDuration}", game.Desk.DeskID, game.Limits.MaxGameDuration);
            return game.FindAgreedGameover() is null
                ? new RelayStep([], [], Finishes: true, Wait: null, TimedOutUids: EndTimedOutFight(game))
                : new RelayStep([], EndFight(game), Finishes: false, Wait: null);
        }

        sends.AddRange(ExpireVotes(game));
        if (game.FirstGameoverAt is { } firstGameoverAt)
        {
            var sinceFirst = clock.GetElapsedTime(firstGameoverAt);
            if (sinceFirst >= StragglerTimeout)
            {
                logger.LogWarning("Desk {DeskID}: trainers {Uids} did not report the game over within {StragglerTimeout}", game.Desk.DeskID, game.Stragglers.Select(seat => seat.Uid), StragglerTimeout);
                return new RelayStep([.. sends], EndFight(game), Finishes: false, Wait: null);
            }

            if (!game.StragglersNoticed && sinceFirst >= StragglerNoticeDelay && game.FindAgreedGameover() is { } agreed)
            {
                game.StragglersNoticed = true;
                var notice = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultGameCltGameOverNtf, new pbSCMultGameCltGameOverNtf
                {
                    BeSurrender = game.SurrenderWinCampClient is null ? NotSurrender : SurrenderEnded,
                    WinCampClient = agreed.BattleParam.WinCampClient,
                });
                sends.AddRange(game.Stragglers.Select(seat => new RelaySend(seat.Uid, RelayChannel.Stream, [notice])));
            }
        }

        if (!game.RunsFrameLoop)
        {
            return new RelayStep([.. sends], completions, Finishes: false, LoadTickInterval);
        }

        var due = (int)(fightElapsed.TotalMilliseconds / KFrapsFreqMs);
        if (due > game.KFrapsNo)
        {
            ApplyTakeover(game, now);
            var firstNew = game.KFrapsNo + 1;
            while (game.KFrapsNo < due)
            {
                game.EmitFrame(game.KFrapsNo == 0 ? FirstFrapBootSession : game.FrapBootSession);
                DecayFrapBootSession(game);
            }

            AddFrameSends(game, firstNew, sends);
        }

        var nextDue = TimeSpan.FromMilliseconds((double)(game.KFrapsNo + 1) * KFrapsFreqMs);
        var wait = nextDue - clock.GetElapsedTime(game.FightStartedAt);
        return new RelayStep([.. sends], completions, Finishes: false, wait > MinFrameWait ? wait : MinFrameWait);
    }

    private static void AddFrameSends(RelayGame game, int firstNew, List<RelaySend> sends)
    {
        var packets = new List<GamePacket>();
        for (var chunkStart = firstNew; chunkStart <= game.KFrapsNo; chunkStart += MaxSpareFrapNum)
        {
            var chunkEnd = Math.Min(chunkStart + MaxSpareFrapNum - 1, game.KFrapsNo);
            var oldest = Math.Max(1, Math.Min(chunkStart, chunkEnd - RedundantFrapNum + 1));
            packets.Add(BuildFramesPacket(game, chunkEnd, oldest));
        }

        var receivers = game.ConnectedSeats.Where(seat => seat.LoadFinished || seat.HasBeginfight).ToList();
        var extraFrames = receivers.Any(seat => seat.IsLossy) ? Math.Max(0, game.FrapBootSession + 1 - RedundantFrapNum) : 0;
        var oldestExtra = Math.Max(1, game.KFrapsNo - RedundantFrapNum - extraFrames + 1);
        var extraPackets = new List<GamePacket>();
        for (var newest = game.KFrapsNo - RedundantFrapNum; newest >= oldestExtra; newest -= RedundantFrapNum)
        {
            extraPackets.Add(BuildFramesPacket(game, newest, Math.Max(oldestExtra, newest - RedundantFrapNum + 1)));
        }

        extraPackets.Reverse();
        GamePacket[] framePackets = [.. packets];
        GamePacket[] lossyFramePackets = [.. extraPackets, .. packets];
        sends.AddRange(receivers.Select(seat => new RelaySend(seat.Uid, RelayChannel.Unreliable, seat.IsLossy ? lossyFramePackets : framePackets)));
    }

    private static GamePacket BuildFramesPacket(RelayGame game, int newest, int oldest)
    {
        var ntf = new pbSCFrapBootInfoNtf { Session = newest == oldest && newest == 1 ? FirstFrapBootSession : game.FrapBootSession };
        for (var kFrapsNo = newest; kFrapsNo >= oldest; kFrapsNo--)
        {
            ntf.SpareFrap.Add(game.FindFrame(kFrapsNo)!);
        }

        return BuildFrapBootInfoPacket(ntf, game.TakeSvrPkgSeq());
    }

    private static void MarkLoss(RelayGame game, RelaySeat seat)
    {
        seat.IsLossy = true;
        game.FrapBootSession = LossFrapBootSession;
        game.FrapBootSessionSince = game.KFrapsNo;
    }

    private static void DecayFrapBootSession(RelayGame game)
    {
        if (game.FrapBootSession <= FrapBootSession || game.KFrapsNo - game.FrapBootSessionSince < FrapBootSessionStepFrames)
        {
            return;
        }

        game.FrapBootSession--;
        game.FrapBootSessionSince = game.KFrapsNo;
        if (game.FrapBootSession == FrapBootSession)
        {
            foreach (var seat in game.Seats.Values)
            {
                seat.IsLossy = false;
            }
        }
    }

    private void ApplyTakeover(RelayGame game, long now)
    {
        var limits = game.Limits;
        foreach (var seat in game.Seats.Values.Where(seat => !seat.QuitByExitButton))
        {
            var silence = clock.GetElapsedTime(seat.LastHeardAt, now);
            if (!seat.IsConnected || silence >= LostSilence)
            {
                seat.EverLost = true;
            }

            if (!seat.DisconnInjected && silence >= DisconnSilence)
            {
                InjectAcntState(game, seat, PbCsFrapbootAcntstateDisconn);
                seat.DisconnInjected = true;
            }

            var idle = clock.GetElapsedTime(seat.LastCommandAt, now);
            switch (seat.Takeover)
            {
                case RelaySeatTakeover.None when seat.EverLost && (limits.Disconned2HangupTime ?? limits.Disconned2AiHostingTime) is { } limit && idle >= limit:
                    InjectAcntState(game, seat, PbCsFrapbootAcntstateAiStart);
                    seat.Takeover = RelaySeatTakeover.AiHosted;
                    break;
                case RelaySeatTakeover.None when !seat.EverLost && limits.Afk2HangupTime is { } limit && idle >= limit:
                    InjectAcntState(game, seat, PbCsFrapbootAcntstateHangup);
                    seat.Takeover = RelaySeatTakeover.Hangup;
                    break;
                case RelaySeatTakeover.Hangup when limits.Afk2AiHostingTime is { } limit && idle >= limit:
                    InjectAcntState(game, seat, PbCsFrapbootAcntstateAiStart);
                    seat.Takeover = RelaySeatTakeover.AiHosted;
                    break;
            }
        }
    }

    private void Heard(RelaySeat seat) => seat.LastHeardAt = clock.GetTimestamp();

    private async Task ExpireUnattachedAsync(BattleDesk desk)
    {
        try
        {
            await Task.Delay(AttachTimeout, clock);
            bool expired;
            ulong[] releasedUids = [];
            lock (gate)
            {
                expired = gamesByDesk.GetValueOrDefault(desk.DeskID)?.Desk != desk && desks.Finish(desk, out releasedUids);
            }

            if (expired)
            {
                logger.LogWarning("Desk {DeskID} was closed: no trainer reached the relay within {AttachTimeout}", desk.DeskID, AttachTimeout);
                await RaiseRelayFinishedAsync(releasedUids);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Closing unattached desk {DeskID} failed", desk.DeskID);
        }
    }

    private async Task FinishAsync(RelayGame game)
    {
        ulong[] releasedUids;
        lock (gate)
        {
            if (gamesByDesk.GetValueOrDefault(game.Desk.DeskID) == game)
            {
                gamesByDesk.Remove(game.Desk.DeskID);
            }

            foreach (var uid in game.Desk.HumanUids.Where(uid => gamesByUid.GetValueOrDefault(uid) == game))
            {
                gamesByUid.Remove(uid);
            }

            desks.Finish(game.Desk, out releasedUids);
        }

        StopGame(game);
        await RaiseRelayFinishedAsync(releasedUids);
    }

    private void StopGame(RelayGame game)
    {
        ulong[] connected;
        lock (game.Gate)
        {
            if (game.Finished)
            {
                return;
            }

            game.Finished = true;
            connected = game.ConnectedUids;
            foreach (var seat in game.Seats.Values)
            {
                seat.IsConnected = false;
            }
        }

        game.Stop();
        foreach (var uid in connected)
        {
            relayRoles.Close(uid);
        }
    }

    private Task RaiseRelayFinishedAsync(IEnumerable<ulong> releasedUids) =>
        UidEvents.RaiseEachAsync(RelayFinished, releasedUids, logger, nameof(RelayFinished));

    private GamePacket? BuildConnRelayRecover(ulong uid)
    {
        if (desks.FindRelayDesk(uid) is not { } desk || desk.FindHuman(uid) is not { } human || desk.HumanUids.Length < 2)
        {
            return null;
        }

        RelayGame? game;
        lock (gate)
        {
            game = gamesByDesk.GetValueOrDefault(desk.DeskID) is { } existing && existing.Desk == desk ? existing : null;
        }

        if (game is not null)
        {
            lock (game.Gate)
            {
                if (game.Finished || game.Phase == RelayGamePhase.Over)
                {
                    return null;
                }
            }
        }

        logger.LogInformation("Uid {Uid} logged in while its desk {DeskID} is on the relay; it is offered the rejoin", uid, desk.DeskID);
        return GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidConnRelayRecoverNtf, desks.BuildConnRelayRecoverNtf(desk, human));
    }

    private RelayGame CreateGame(BattleDesk desk)
    {
        var npcLoadSeconds = desk.Players
            .Where(player => player.IsNpc)
            .ToDictionary(player => player.ObjId, _ => Random.Shared.Next(MinNpcLoadSeconds, MaxNpcLoadSeconds + 1));
        return new RelayGame(desk, Random.Shared.Next(1, MaxRandomSeed), clock.GetTimestamp(), npcLoadSeconds, FindTimeLimits(desk.DeskInfo.MapId));
    }

    private RelayTimeLimits FindTimeLimits(int mapId)
    {
        var scene = gameData.SceneMap.Find(mapId);
        var matchTime = scene is not null && gameData.MapBase.Find(scene.MapBaseId) is { MatchTime: > 0 } mapBase
            ? TimeSpan.FromMilliseconds(mapBase.MatchTime)
            : (TimeSpan?)null;
        var surrenderStart = scene?.SurrenderStartTime ?? 0;
        var minGameSeconds = matchTime is { } match ? Math.Min(surrenderStart, match.TotalSeconds) : 0;
        var hangupSchemeID = scene?.HangupSchemeID ?? 0;
        var surrender = new SurrenderRules(
            scene is { SurrenderTimesLimit: > 0 } && surrenderStart < DisabledSurrenderStartTime ? surrenderStart : int.MaxValue,
            scene?.SurrenderCDTime ?? 0,
            scene?.SurrenderInvalidTime ?? 0,
            scene?.SurrenderTimesLimit ?? 0);
        return new RelayTimeLimits(
            scene is { Timeout: > 0 } ? TimeSpan.FromSeconds(scene.Timeout)
                : matchTime is { } limit ? limit + GameTimeMargin
                : FallbackMaxGameDuration,
            minGameSeconds,
            FindHangupSeconds(hangupSchemeID, Disconned2HangupTimeKey),
            FindHangupSeconds(hangupSchemeID, Afk2HangupTimeKey),
            FindHangupSeconds(hangupSchemeID, Disconned2AiHostingTimeKey),
            FindHangupSeconds(hangupSchemeID, Afk2AiHostingTimeKey),
            surrender);
    }

    private TimeSpan? FindHangupSeconds(int hangupSchemeID, int keyID) =>
        gameData.HangupScheme.Find(pbHangupScheme.Key(hangupSchemeID, keyID)) is { Value: > 0 } row ? TimeSpan.FromSeconds(row.Value) : null;

    private RelayGame? FindGame(ulong uid)
    {
        lock (gate)
        {
            return gamesByUid.GetValueOrDefault(uid);
        }
    }

    private (RelayGame Game, RelaySeat Seat)? FindSeat(ulong uid) =>
        FindGame(uid) is { } game && game.FindSeat(uid) is { } seat ? (game, seat) : null;

    private static GamePacket BuildFrapBootInfoPacket(pbSCFrapBootInfoNtf ntf, int svrPkgSeq) =>
        new((int)PB_ENUM_CS_MSGID_DEF.PbScidFrapBootInfoNtf, Uid: 0, ClientSeq: 0, ntf.ToNoTagByteArray())
        {
            SvrPkgSeq = svrPkgSeq,
            MsgIDInBodyOnly = true,
        };

    private static GamePacket BuildBeginLoadPacket(RelayGame game) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultgameBeginLoadNtf, BuildBeginLoadNtf(game)) with { ClientSeq = BeginLoadClientSeq };

    private static void InjectSurrenderEnd(RelayGame game, int objId, int winCampClient) =>
        game.PendingBootInfo.Add(new pbCSDT_FRAPBOOT_CC
        {
            ObjID = objId,
            SyncType = SurrenderEndSyncType,
            Buff = ByteString.CopyFrom(new FrameCmdPkg { Type = CmdInfoCase.StFrameCmdSurrenderEnd, IntParam1 = winCampClient }.ToNoTagByteArray()),
        });

    private static GamePacket BuildBeginfightPacket(RelayGame game) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultgameBeginfightNtf, new pbSCMultgameBeginfightNtf
        {
            GameStartTime = game.GameStartTime,
            IsOpenCCSyncMove = OpenCCSyncMove,
            IsSlowUpMoveCmd = SlowUpMoveCmd,
            MaxCltCacheFrapNum = MaxCltCacheFrapNum,
        }) with
        { ClientSeq = BeginfightClientSeq };

    private static IEnumerable<int> BroadcastNpcObjIds(RelayGame game) =>
        game.BroadcastsNpcProcess ? game.NpcLoadSeconds.Keys : [];

    private static IEnumerable<GamePacket> BuildCurrentLoadNotices(RelayGame game, ulong uid) =>
        game.Desk.Players
            .Where(player => player.IsNpc ? game.BroadcastsNpcProcess : player.Uid != uid)
            .Select(player => BuildLoadprocessNtf(player.ObjId, game.Processes.GetValueOrDefault(player.ObjId)));

    private static GamePacket BuildLoadprocessNtf(int objId, int process) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultgameLoadprocessNtf, new pbSCMultgameLoadprocessNtf { ObjId = objId, Process = process });

    private static pbCSDT_CAMPPLAYERINFO BuildLoadCampPlayerInfo(DeskPlayer player)
    {
        var campPlayerInfo = player.CampPlayerInfo.Clone();
        if (!player.IsNpc)
        {
            campPlayerInfo.UsableHero = null;
            campPlayerInfo.UsablePasterList = null;
            campPlayerInfo.UsableTrainerItemList = null;
            campPlayerInfo.PlayerInfo.PickInfo = null;
        }

        return campPlayerInfo;
    }

    private static pbSCMultgameBeginLoadNtf BuildBeginLoadNtf(RelayGame game)
    {
        var desk = game.Desk;
        int[] camps = [.. desk.Players.Select(player => player.CampClient).Distinct().Order()];
        return new pbSCMultgameBeginLoadNtf
        {
            DeskId = desk.DeskID,
            DeskSeq = desk.DeskCrtSeq,
            KFrapsLater = KFrapsLater,
            KFrapsFreqMs = KFrapsFreqMs,
            PreActFrap = PreActFrap,
            RandomSeed = game.RandomSeed,
            GameType = desk.DeskInfo.GameType,
            DeskInfo = desk.DeskInfo.Clone(),
            PlayerInfo = new pbCSDT_DESK_PLAYERINFO { CampPlayerInfo = { desk.Players.Select(BuildLoadCampPlayerInfo) } },
            HaskChkFreq = HaskChkFreq,
            CltLogSize = CltLogSize,
            VirtualClient = new pbCSDT_DESK_VIRTUAL_CLIENT(),
            IsNpcProcessBroadcast = game.BroadcastsNpcProcess ? NpcProcessBroadcast : NoNpcProcessBroadcast,
            SingleAcntSwitch = game.IsSingleAcnt ? SingleAcntOn : SingleAcntOff,
            CanopyShowList = new pbCOMDT_CANOPY_SHOW_LIST
            {
                CanopyShowList = { camps.Select(camp => new pbCOMDT_CANOPY_SHOW_NODE { CanopyID = DefaultCanopyID, CampClient = camp, ShowTm = game.LoadStartTime }) },
            },
            TeamCampBuffTypeList = new pbCOMDT_TEAM_CAMP_BUFF_LIST
            {
                TeamCampBuffInfo = { camps.Select(camp => new pbCOMDT_TEAM_CAMP_BUFF_INFO { CampClient = camp }) },
            },
            MsgUseNoTag = MsgUseNoTag,
            PveChallengeSelectNode = desk.PveChallengeSelectNode?.Clone() ?? new pbCOMDT_PVE_CHALLENGE_SELECT_BATTLE(),
        };
    }

    private static pbResultInfo Refusal(PB_ENUM_RES_ERROR_CODE code) => new() { ResultId = (int)code };

    private static bool IsPrintableHost(string host) =>
        host.Length <= MaxReportedRelayIPLength && host.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or ':' or '-');

    private async Task SendAllAsync(IEnumerable<RelaySend> sends)
    {
        foreach (var send in sends)
        {
            await relayRoles.PushAsync(send.Uid, send.Channel, send.Packets);
        }
    }

    private sealed record RelaySend(ulong Uid, RelayChannel Channel, GamePacket[] Packets);

    private sealed record RelayStep(RelaySend[] Sends, List<SeatCompletion> Completions, bool Finishes, TimeSpan? Wait, ulong[]? TimedOutUids = null);

    private sealed record SeatCompletion(BattleResult Result, bool SendsFinalRsp);

    private sealed record GameoverFault(PB_ENUM_RES_ERROR_CODE Code, string Reason);

    private long NextWholeSecond() =>
        (clock.GetUtcNow().ToUnixTimeMilliseconds() + (long)MillisecondsPerSecond - 1) / (long)MillisecondsPerSecond;
}
