using Google.Protobuf;
using Microsoft.Extensions.Logging;
using SpiritUNITE.Online;
using SpiritUNITE.Packets;
using SpiritUNITE.Packets.Messages;
using SpiritUNITE.Tools;

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
    private const int SingleAcntOn = 1;
    private const int SingleAcntOff = 0;
    private const int MsgUseTag = 0;
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
    private const int MaxNpcLoadSeconds = 18;
    private const int MaxSpareFrapNum = 9;
    private const int MaxPendingBootInfo = 256;
    private const int AcntObjType = 1;
    private const int MinGameSeconds = 60;
    private const int UsedTimeToleranceSeconds = 10;
    private const double MillisecondsPerSecond = 1000;
    private const int RepairPkgSeq = 0;
    private const int MaxUperBuffBytes = 512;
    private const int MaxBootInfoPerObjPerFrame = 16;
    private const int MaxBootInfoBytesPerObjPerFrame = 1024;
    private const int MaxRepairRanges = 64;
    private const int MaxRepairBufBytes = 48 * 1024;
    private const int MaxReportedRelayIPLength = 64;
    private const int SecondGameoverRspVersion = 1;

    private static readonly TimeSpan LoadTickInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(KFrapsFreqMs);
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan SecondGameoverRspDelay = TimeSpan.FromMilliseconds(520);
    private static readonly TimeSpan SettlementDelay = TimeSpan.FromMilliseconds(230);
    private static readonly TimeSpan FinishDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AbandonDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan AttachTimeout = LoadTimeout;
    private static readonly TimeSpan MaxGameDuration = TimeSpan.FromMinutes(30);

    private readonly DeskManager desks;
    private readonly BattleSettlement settlement;
    private readonly RelayRoles relayRoles;
    private readonly TimeProvider clock;
    private readonly ILogger<RelayManager> logger;
    private readonly Lock gate = new();
    private readonly Dictionary<int, RelayGame> gamesByDesk = [];
    private readonly Dictionary<ulong, RelayGame> gamesByUid = [];

    public event Func<ulong, Task>? RelayFinished;

    public RelayManager(DeskManager desks, BattleSettlement settlement, RelayRoles relayRoles, TimeProvider clock, ILogger<RelayManager> logger)
    {
        this.desks = desks;
        this.settlement = settlement;
        this.relayRoles = relayRoles;
        this.clock = clock;
        this.logger = logger;
        relayRoles.RelayOnline += AttachAsync;
        relayRoles.RelayOffline += DetachAsync;
        desks.RelayHandedOff += desk => _ = ExpireUnattachedAsync(desk);
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

        GamePacket[] packets = [];
        bool finished;
        lock (game.Gate)
        {
            finished = game.Finished;
            if (!finished)
            {
                game.ConnectedUids.Add(uid);
                packets = [.. BuildAttachNotices(game, uid, created)];
            }
        }

        if (finished)
        {
            relayRoles.Close(uid);
            return;
        }

        logger.LogInformation("Uid {Uid} joined relay desk {DeskID}", uid, game.Desk.DeskID);
        if (packets.Length > 0)
        {
            await relayRoles.PushAsync(uid, RelayChannel.Stream, packets);
        }

        if (created)
        {
            _ = RunLoadAsync(game);
        }
    }

    private async Task DetachAsync(ulong uid)
    {
        if (FindGame(uid) is not { } game)
        {
            return;
        }

        bool finishes;
        bool endsFight;
        bool abandons;
        lock (game.Gate)
        {
            if (!game.ConnectedUids.Remove(uid))
            {
                return;
            }

            endsFight = !game.Finished && game.EndFightIfAllReported();
            finishes = !game.Finished && game.Phase == RelayGamePhase.Over && game.AllHumansLeft;
            abandons = !game.Finished && game.Phase != RelayGamePhase.Over;
        }

        logger.LogInformation("Uid {Uid} left relay desk {DeskID}", uid, game.Desk.DeskID);
        if (finishes)
        {
            await FinishAsync(game);
        }
        else if (endsFight)
        {
            logger.LogInformation("Desk {DeskID}: every remaining trainer reported the game over", game.Desk.DeskID);
            _ = FinishAfterDelayAsync(game);
        }
        else if (abandons)
        {
            _ = AbandonAsync(game, uid);
        }
    }

    public async Task LoadProcessAsync(ulong uid, pbCSMultgameLoadprocessReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } human) || request.Process is < 0 or > RelayGame.FullProcess)
        {
            return;
        }

        ulong[] others;
        lock (game.Gate)
        {
            if (game.Phase != RelayGamePhase.Loading)
            {
                return;
            }

            game.Processes[human.ObjId] = request.Process;
            others = [.. game.ConnectedUids.Where(other => other != uid)];
        }

        var notice = BuildLoadprocessNtf(human.ObjId, request.Process);
        foreach (var other in others)
        {
            await relayRoles.PushAsync(other, RelayChannel.Stream, notice);
        }
    }

    public pbSCMultgameLoadfinRsp LoadFinished(ulong uid)
    {
        if (FindSeat(uid) is not ({ } game, _))
        {
            return new pbSCMultgameLoadfinRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        lock (game.Gate)
        {
            game.LoadFinishedUids.Add(uid);
            return new pbSCMultgameLoadfinRsp { State = (int)game.ReconnState };
        }
    }

    public pbSCReConnGameRsp ReConnGame(ulong uid)
    {
        if (FindSeat(uid) is not ({ } game, { } human))
        {
            return new pbSCReConnGameRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        lock (game.Gate)
        {
            return new pbSCReConnGameRsp
            {
                SelfObjID = human.ObjId,
                State = (int)game.ReconnState,
                CampSurrenderInfo = new pbCOMDT_CAMP_SURRENDER_INFO(),
                AcntSurrenderInfo = new pbCOMDT_ACNT_SURRENDER_INFO(),
                VoiceAccusationInfo = new pbCOMDT_ACNT_VOICE_ACCUSATION_INFO(),
                PauseType = PauseType,
                CurFrapNo = game.KFrapsNo,
                PauseCancelType = PauseCancelType,
            };
        }
    }

    public async Task PingAsync(ulong uid, GamePacket ping)
    {
        if (FindSeat(uid) is not ({ } game, _))
        {
            return;
        }

        bool echoes;
        lock (game.Gate)
        {
            echoes = game.Phase == RelayGamePhase.Fighting;
        }

        if (echoes)
        {
            await relayRoles.PushAsync(uid, RelayChannel.Unreliable, ping with { Uid = 0, ClientSeq = 0 });
        }
    }

    public pbSCMultGameAliveStateRsp AliveState(ulong uid, pbCSMultGameAliveStateReq request)
    {
        if (FindSeat(uid) is not ({ } game, _) || game.Desk.FindPlayer(request.AcntObjID) is null)
        {
            return new pbSCMultGameAliveStateRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        logger.LogDebug("Uid {Uid} reports object {AcntObjID} alive state {AliveState}", uid, request.AcntObjID, request.AliveState);
        return new pbSCMultGameAliveStateRsp { ResultInfo = new pbResultInfo() };
    }

    public void HangUp(ulong uid, pbCSOpeningGameHangUpReq request) =>
        logger.LogInformation("Uid {Uid} reports opening hang-up of uids {HangUpAcntUid}", uid, request.HangUpAcntUid);

    public void HashCheck(ulong uid, pbCSRelayHashcheckReq request) =>
        logger.LogDebug("Uid {Uid} hash check at frame {KFrapsNo}", uid, request.KFrapsNo);

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

    public void Upermsg(ulong uid, pbCSGamingUpermsgReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } human))
        {
            return;
        }

        lock (game.Gate)
        {
            if (!game.RunsFrameLoop || game.Phase != RelayGamePhase.Fighting)
            {
                return;
            }

            var objId = human.ObjId;
            var count = game.PendingBootCountByObjId.GetValueOrDefault(objId);
            var bytes = game.PendingBootBytesByObjId.GetValueOrDefault(objId);
            var dropped = 0;
            foreach (var uper in request.UperInfo.Where(uper => uper.ObjID == objId))
            {
                if (game.PendingBootInfo.Count >= MaxPendingBootInfo
                    || count >= MaxBootInfoPerObjPerFrame
                    || uper.Buff.Length > MaxUperBuffBytes
                    || bytes + uper.Buff.Length > MaxBootInfoBytesPerObjPerFrame)
                {
                    dropped++;
                    continue;
                }

                game.PendingBootInfo.Add(new pbCSDT_FRAPBOOT_CC { ObjID = objId, SyncType = uper.SyncType, Buff = uper.Buff });
                count++;
                bytes += uper.Buff.Length;
            }

            game.PendingBootCountByObjId[objId] = count;
            game.PendingBootBytesByObjId[objId] = bytes;
            if (dropped > 0)
            {
                logger.LogDebug("Desk {DeskID}: dropped {Dropped} frame commands of uid {Uid} over the per-frame limits", game.Desk.DeskID, dropped, uid);
            }
        }
    }

    public pbCSFrapBootRepairRsp RepairFrames(ulong uid, pbCSFrapBootRepairReq request)
    {
        if (FindSeat(uid) is not ({ } game, _))
        {
            return new pbCSFrapBootRepairRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        lock (game.Gate)
        {
            var buf = new List<byte>();
            foreach (var chunk in FindRepairFrames(game, request).Chunk(MaxSpareFrapNum))
            {
                var packet = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFrapBootInfoNtf, new pbSCFrapBootInfoNtf
                {
                    Session = request.Session,
                    SpareFrap = { chunk },
                }).ToByteArray(RepairPkgSeq);
                if (buf.Count + packet.Length > MaxRepairBufBytes)
                {
                    break;
                }

                buf.AddRange(packet);
            }

            return new pbCSFrapBootRepairRsp
            {
                ResultInfo = new pbResultInfo(),
                MaxFrapNo = game.KFrapsNo,
                Buf = ByteString.CopyFrom([.. buf]),
            };
        }
    }

    private static List<pbSCFrapBootSingleNtf> FindRepairFrames(RelayGame game, pbCSFrapBootRepairReq request)
    {
        var seen = new HashSet<int>();
        var frames = new List<pbSCFrapBootSingleNtf>();
        foreach (var range in request.KFrapsNo.Take(MaxRepairRanges))
        {
            var first = Math.Max(range.KFrapsNo, game.FirstStoredKFrapsNo);
            var last = (int)Math.Min((long)range.KFrapsNo + Math.Clamp(range.KFrapsNum, 0, MaxCltCacheFrapNum) - 1, game.KFrapsNo);
            for (var kFrapsNo = first; kFrapsNo <= last && frames.Count < MaxCltCacheFrapNum; kFrapsNo++)
            {
                if (seen.Add(kFrapsNo) && game.FindFrame(kFrapsNo) is { } frame)
                {
                    frames.Add(frame);
                }
            }
        }

        return frames;
    }

    public pbSCMultgameGameoverRsp Gameover(ulong uid, pbCSMultgameGameoverReq request)
    {
        if (FindSeat(uid) is not ({ } game, { } human))
        {
            return new pbSCMultgameGameoverRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrAccountNotInDesk) };
        }

        BattleResult result;
        lock (game.Gate)
        {
            if (game.GameoverUids.Contains(uid))
            {
                return new pbSCMultgameGameoverRsp { ResultInfo = new pbResultInfo(), IsCltNeedWait = CltNeedWait };
            }

            var fightSeconds = game.Phase == RelayGamePhase.Loading ? 0 : clock.GetElapsedTime(game.FightStartedAt).TotalSeconds;
            if (game.Phase == RelayGamePhase.Loading || game.Finished)
            {
                logger.LogWarning("Desk {DeskID}: uid {Uid} sent a game over in phase {Phase}", game.Desk.DeskID, uid, game.Phase);
                return new pbSCMultgameGameoverRsp { ResultInfo = Refusal(PB_ENUM_RES_ERROR_CODE.PbErrDeskStateInvalid) };
            }

            if (FindGameoverFault(game.Desk, human, request, fightSeconds) is { } fault)
            {
                logger.LogWarning("Desk {DeskID}: refused the game over of uid {Uid}: {Fault}", game.Desk.DeskID, uid, fault.Reason);
                return new pbSCMultgameGameoverRsp { ResultInfo = Refusal(fault.Code) };
            }

            game.GameoverUids.Add(uid);
            game.AgreedGameover ??= request;
            if (game.AgreedGameover.BattleParam.WinCampClient != request.BattleParam.WinCampClient)
            {
                logger.LogWarning(
                    "Desk {DeskID}: uid {Uid} reports winner camp {WinCampClient}, but the agreed winner is camp {AgreedWinCampClient}",
                    game.Desk.DeskID,
                    uid,
                    request.BattleParam.WinCampClient,
                    game.AgreedGameover.BattleParam.WinCampClient);
            }

            var agreed = request.Clone();
            agreed.BattleParam = game.AgreedGameover.BattleParam.Clone();
            agreed.GameoverSummary = game.AgreedGameover.GameoverSummary?.Clone();
            result = new BattleResult(game.Desk, human, agreed, game.GameStartTime, (int)Math.Round(fightSeconds));
            if (game.EndFightIfAllReported())
            {
                logger.LogInformation("Desk {DeskID}: every trainer on the relay reported the game over", game.Desk.DeskID);
            }
        }

        logger.LogInformation(
            "Desk {DeskID}: uid {Uid} reports game over after {GameTime} s, winner camp {WinCampClient}",
            game.Desk.DeskID,
            uid,
            result.GameTime,
            result.Gameover.BattleParam.WinCampClient);
        _ = CompleteGameoverAsync(game, result);
        return new pbSCMultgameGameoverRsp { ResultInfo = new pbResultInfo(), IsCltNeedWait = CltNeedWait };
    }

    private static GameoverFault? FindGameoverFault(BattleDesk desk, DeskPlayer human, pbCSMultgameGameoverReq request, double fightSeconds)
    {
        var param = request.BattleParam;
        if (param is null || param.MapID != desk.DeskInfo.MapId || param.GameType != desk.DeskInfo.GameType)
        {
            return new GameoverFault(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, "the battle parameters do not match the desk");
        }

        if (!desk.Players.Any(player => player.CampClient == param.WinCampClient))
        {
            return new GameoverFault(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, $"winner camp {param.WinCampClient} is not on the desk");
        }

        if (!request.AcntInfo.Any(info => info.AcntUid == human.Uid && info.ObjType == AcntObjType && info.AcntObjID == human.ObjId))
        {
            return new GameoverFault(PB_ENUM_RES_ERROR_CODE.PbErrCommonParamInvalid, "the sender's own entry is missing");
        }

        if (fightSeconds < MinGameSeconds || param.UsedTime / MillisecondsPerSecond > fightSeconds + UsedTimeToleranceSeconds)
        {
            return new GameoverFault(
                PB_ENUM_RES_ERROR_CODE.PbErrJudgeIllegalOperation,
                $"used time {param.UsedTime} ms does not fit {fightSeconds:F0} s of fighting");
        }

        return null;
    }

    private async Task CompleteGameoverAsync(RelayGame game, BattleResult result)
    {
        try
        {
            await Task.Delay(SecondGameoverRspDelay, clock);
            await relayRoles.PushAsync(
                result.Human.Uid,
                RelayChannel.Stream,
                GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultgameGameoverRsp, new pbSCMultgameGameoverRsp()) with
                {
                    Version = SecondGameoverRspVersion,
                });

            await Task.Delay(SettlementDelay, clock);
            try
            {
                await settlement.SettleAsync(result);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Settlement of uid {Uid} on desk {DeskID} failed", result.Human.Uid, game.Desk.DeskID);
            }

            await Task.Delay(FinishDelay, clock);
            bool finishes;
            lock (game.Gate)
            {
                finishes = game.Phase == RelayGamePhase.Over;
            }

            if (finishes)
            {
                await FinishAsync(game);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Game over of uid {Uid} on desk {DeskID} failed", result.Human.Uid, game.Desk.DeskID);
        }
    }

    private async Task RunLoadAsync(RelayGame game)
    {
        try
        {
            using var timer = new PeriodicTimer(LoadTickInterval, clock);
            while (await timer.WaitForNextTickAsync(game.Stopping.Token))
            {
                var tick = AdvanceLoad(game);
                await SendAllAsync(tick.Sends);
                if (tick.BeginsFight)
                {
                    logger.LogInformation("Desk {DeskID} begins the fight", game.Desk.DeskID);
                    _ = LimitGameDurationAsync(game);
                    if (game.RunsFrameLoop)
                    {
                        await RunFramesAsync(game);
                    }

                    return;
                }

                if (tick.TimedOut)
                {
                    logger.LogWarning("Desk {DeskID} did not finish loading in {Timeout}", game.Desk.DeskID, LoadTimeout);
                    await FinishAsync(game);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (game.Stopping.IsCancellationRequested)
        {
            logger.LogDebug("Desk {DeskID} relay loop stopped", game.Desk.DeskID);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Relay loop of desk {DeskID} failed", game.Desk.DeskID);
            await FinishAsync(game);
        }
    }

    private LoadTick AdvanceLoad(RelayGame game)
    {
        lock (game.Gate)
        {
            if (game.Phase != RelayGamePhase.Loading || game.Finished)
            {
                return new LoadTick([], BeginsFight: false, TimedOut: false);
            }

            game.LoadTick++;
            foreach (var objId in game.NpcLoadSeconds.Keys)
            {
                game.Processes[objId] = game.NpcProcessAt(objId, game.LoadTick);
            }

            var beginsFight = game.AllNpcsLoaded && game.AllHumansLoaded;
            var notices = game.NpcLoadSeconds.Keys
                .Where(_ => beginsFight || Random.Shared.Next(2) == 0)
                .Order()
                .Select(objId => BuildLoadprocessNtf(objId, game.Processes[objId]))
                .ToList();
            if (beginsFight)
            {
                game.Phase = RelayGamePhase.Fighting;
                game.GameStartTime = clock.GetUtcNow().ToUnixTimeSeconds();
                game.FightStartedAt = clock.GetTimestamp();
                var beginfight = new pbSCMultgameBeginfightNtf
                {
                    GameStartTime = game.GameStartTime,
                    IsOpenCCSyncMove = OpenCCSyncMove,
                    IsSlowUpMoveCmd = SlowUpMoveCmd,
                    MaxCltCacheFrapNum = MaxCltCacheFrapNum,
                };
                notices.Add(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultgameBeginfightNtf, beginfight) with { ClientSeq = BeginfightClientSeq });
            }

            var timedOut = !beginsFight && clock.GetElapsedTime(game.LoadStartedAt) > LoadTimeout;
            return new LoadTick([.. game.ConnectedUids.Select(uid => new RelaySend(uid, RelayChannel.Stream, [.. notices]))], beginsFight, timedOut);
        }
    }

    private async Task RunFramesAsync(RelayGame game)
    {
        using var timer = new PeriodicTimer(FrameInterval, clock);
        while (await timer.WaitForNextTickAsync(game.Stopping.Token))
        {
            RelaySend[] sends;
            lock (game.Gate)
            {
                if (game.Phase != RelayGamePhase.Fighting || game.Finished)
                {
                    return;
                }

                game.KFrapsNo++;
                var info = new pbCSDT_FRAPBOOT_INFO { KFrapsNo = game.KFrapsNo };
                game.TakePendingBootInfo(info);
                var frame = new pbSCFrapBootSingleNtf
                {
                    KFrapsNo = game.KFrapsNo,
                    InfoBuff = info.ToByteString(),
                };
                game.StoreFrame(frame);
                var packet = GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidFrapBootSingleNtf, frame);
                sends = [.. game.ConnectedUids.Select(uid => new RelaySend(uid, RelayChannel.Unreliable, [packet]))];
            }

            await SendAllAsync(sends);
        }
    }

    private async Task AbandonAsync(RelayGame game, ulong uid)
    {
        try
        {
            await Task.Delay(AbandonDelay, clock);
            bool abandoned;
            lock (game.Gate)
            {
                abandoned = !game.Finished && game.AllHumansLeft;
            }

            if (abandoned)
            {
                logger.LogInformation("Desk {DeskID} was abandoned after uid {Uid} left", game.Desk.DeskID, uid);
                await FinishAsync(game);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Abandoning desk {DeskID} failed", game.Desk.DeskID);
        }
    }

    private async Task FinishAfterDelayAsync(RelayGame game)
    {
        try
        {
            await Task.Delay(FinishDelay, clock, game.Stopping.Token);
            await FinishAsync(game);
        }
        catch (OperationCanceledException) when (game.Stopping.IsCancellationRequested)
        {
            logger.LogDebug("Desk {DeskID} finished before its delayed finish", game.Desk.DeskID);
        }
    }

    private async Task LimitGameDurationAsync(RelayGame game)
    {
        try
        {
            await Task.Delay(MaxGameDuration, clock, game.Stopping.Token);
            logger.LogWarning("Desk {DeskID} reached the maximum game duration of {MaxGameDuration} and is closed", game.Desk.DeskID, MaxGameDuration);
            await FinishAsync(game);
        }
        catch (OperationCanceledException) when (game.Stopping.IsCancellationRequested)
        {
            logger.LogDebug("Desk {DeskID} finished within the maximum game duration", game.Desk.DeskID);
        }
    }

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
            connected = [.. game.ConnectedUids];
            game.ConnectedUids.Clear();
        }

        game.Stopping.Cancel();
        foreach (var uid in connected)
        {
            relayRoles.Close(uid);
        }
    }

    private Task RaiseRelayFinishedAsync(IEnumerable<ulong> releasedUids) =>
        UidEvents.RaiseEachAsync(RelayFinished, releasedUids, logger, nameof(RelayFinished));

    private RelayGame CreateGame(BattleDesk desk)
    {
        var npcLoadSeconds = desk.Players
            .Where(player => player.IsNpc)
            .ToDictionary(player => player.ObjId, _ => Random.Shared.Next(MinNpcLoadSeconds, MaxNpcLoadSeconds + 1));
        var loadStartTime = (clock.GetUtcNow().ToUnixTimeMilliseconds() + (long)MillisecondsPerSecond - 1) / (long)MillisecondsPerSecond;
        return new RelayGame(desk, Random.Shared.Next(1, MaxRandomSeed), loadStartTime, clock.GetTimestamp(), npcLoadSeconds);
    }

    private RelayGame? FindGame(ulong uid)
    {
        lock (gate)
        {
            return gamesByUid.GetValueOrDefault(uid);
        }
    }

    private (RelayGame Game, DeskPlayer Human)? FindSeat(ulong uid) =>
        FindGame(uid) is { } game && game.Desk.FindHuman(uid) is { } human ? (game, human) : null;

    private static IEnumerable<GamePacket> BuildAttachNotices(RelayGame game, ulong uid, bool created)
    {
        if (game.Phase != RelayGamePhase.Loading)
        {
            return [];
        }

        var loadNotices = created ? BuildFirstLoadNotices(game) : BuildCurrentLoadNotices(game, uid);
        return game.LoadFinishedUids.Contains(uid) ? loadNotices : loadNotices.Prepend(BuildBeginLoadPacket(game));
    }

    private static GamePacket BuildBeginLoadPacket(RelayGame game) =>
        GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultgameBeginLoadNtf, BuildBeginLoadNtf(game)) with { ClientSeq = BeginLoadClientSeq };

    private static IEnumerable<GamePacket> BuildFirstLoadNotices(RelayGame game) =>
        game.NpcLoadSeconds.Keys
            .Where(_ => Random.Shared.Next(2) == 0)
            .Order()
            .Select(objId => BuildLoadprocessNtf(objId, game.Processes.GetValueOrDefault(objId)));

    private static IEnumerable<GamePacket> BuildCurrentLoadNotices(RelayGame game, ulong uid) =>
        game.Desk.Players
            .Where(player => player.IsNpc || player.Uid != uid)
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
            IsNpcProcessBroadcast = NpcProcessBroadcast,
            SingleAcntSwitch = game.IsSingleAcnt ? SingleAcntOn : SingleAcntOff,
            CanopyShowList = new pbCOMDT_CANOPY_SHOW_LIST
            {
                CanopyShowList = { camps.Select(camp => new pbCOMDT_CANOPY_SHOW_NODE { CanopyID = DefaultCanopyID, CampClient = camp, ShowTm = game.LoadStartTime }) },
            },
            TeamCampBuffTypeList = new pbCOMDT_TEAM_CAMP_BUFF_LIST
            {
                TeamCampBuffInfo = { camps.Select(camp => new pbCOMDT_TEAM_CAMP_BUFF_INFO { CampClient = camp }) },
            },
            MsgUseNoTag = MsgUseTag,
            PveChallengeSelectNode = desk.PveChallengeSelectNode?.Clone(),
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

    private sealed record LoadTick(RelaySend[] Sends, bool BeginsFight, bool TimedOut);

    private sealed record GameoverFault(PB_ENUM_RES_ERROR_CODE Code, string Reason);
}
