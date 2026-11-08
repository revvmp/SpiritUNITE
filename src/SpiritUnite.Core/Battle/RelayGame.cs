using Google.Protobuf;
using SpiritUnite.Database.Repositories;
using SpiritUnite.Game.Match;
using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public enum RelayGamePhase
{
    Waiting,
    Loading,
    Fighting,
    Over,
    Finished,
}

public enum RelaySeatTakeover
{
    None,
    Hangup,
    AiHosted,
}

public enum RelaySettlementPhase
{
    None,
    Queued,
    Processing,
    Completed,
    NeedsReview,
    TimedOut,
}

public sealed record RelayTimeLimits(
    TimeSpan MaxGameDuration,
    double MinGameSeconds,
    TimeSpan? Disconned2HangupTime,
    TimeSpan? Afk2HangupTime,
    TimeSpan? Disconned2AiHostingTime,
    TimeSpan? Afk2AiHostingTime,
    SurrenderRules Surrender);

public sealed class RelaySeat(DeskPlayer human)
{
    private const int RecentCmdSeqCapacity = 128;

    private readonly HashSet<int> recentCmdSeqs = [];
    private readonly Queue<int> recentCmdSeqOrder = new();

    public DeskPlayer Human { get; } = human;
    public ulong Uid => Human.Uid;
    public int ObjId => Human.ObjId;
    public bool IsConnected { get; set; }
    public bool HasBeginLoad { get; set; }
    public bool HasBeginfight { get; set; }
    public bool DisconnInjected { get; set; }
    public bool IsLossy { get; set; }
    public bool LoadFinished { get; set; }
    public long LastCommandAt { get; set; }
    public long LastHeardAt { get; set; }
    public bool EverLost { get; set; }
    public bool QuitByExitButton { get; set; }
    public RelaySeatTakeover Takeover { get; set; }
    public pbCSMultgameGameoverReq? Gameover { get; set; }
    public int GameoverFightSeconds { get; set; }
    public int GameoverHangupSeconds { get; set; }
    public RelaySettlementPhase SettlementPhase { get; private set; }
    public bool HasSettlement => SettlementPhase != RelaySettlementPhase.None;
    public int StartedSurrenderVotes { get; set; }
    public TimeSpan HangupTime { get; private set; }

    public void QueueSettlement()
    {
        if (HasSettlement)
        {
            throw new InvalidOperationException($"Settlement is already {SettlementPhase}");
        }

        SettlementPhase = RelaySettlementPhase.Queued;
    }

    public void RecordSettlement(SettlementState state)
    {
        if (SettlementPhase is not (RelaySettlementPhase.Queued or RelaySettlementPhase.Processing))
        {
            throw new InvalidOperationException($"Cannot record settlement in {SettlementPhase}");
        }

        SettlementPhase = state switch
        {
            SettlementState.Pending => RelaySettlementPhase.Queued,
            SettlementState.Processing => RelaySettlementPhase.Processing,
            SettlementState.Completed => RelaySettlementPhase.Completed,
            SettlementState.NeedsReview => RelaySettlementPhase.NeedsReview,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
    }

    public void TimeOutSettlement()
    {
        if (HasSettlement)
        {
            throw new InvalidOperationException($"Settlement is already {SettlementPhase}");
        }

        SettlementPhase = RelaySettlementPhase.TimedOut;
    }

    public void CountSilence(TimeSpan silence, TimeSpan? hangupAfter)
    {
        if (hangupAfter is { } threshold && silence >= threshold)
        {
            HangupTime += silence;
        }
    }

    public bool TryAcceptCmdSeq(int cmdSeq)
    {
        if (!recentCmdSeqs.Add(cmdSeq))
        {
            return false;
        }

        recentCmdSeqOrder.Enqueue(cmdSeq);
        if (recentCmdSeqOrder.Count > RecentCmdSeqCapacity)
        {
            recentCmdSeqs.Remove(recentCmdSeqOrder.Dequeue());
        }

        return true;
    }

    public void ForgetCmdSeqs()
    {
        recentCmdSeqs.Clear();
        recentCmdSeqOrder.Clear();
    }
}

public sealed record StoredFrame(int SvrPkgSeq, int Session, byte[] InfoBuff);

public sealed class RelayGame
{
    public const int FullProcess = 1000;
    public const long MaxStoredFrameBytes = 64 * 1024 * 1024;
    public const int MaxFrameInfoBytes = 10000;

    private const int FirstSvrPkgSeq = 1;
    private const int NoLoadTick = -1;

    private readonly List<StoredFrame> frames = [];
    private readonly Dictionary<int, Dictionary<ulong, long>> hashesByKFrapsNo = [];
    private readonly CancellationTokenSource stopping = new();
    private int nextSvrPkgSeq = FirstSvrPkgSeq;

    public RelayGame(BattleDesk desk, int randomSeed, long createdAt, IReadOnlyDictionary<int, int> npcLoadSeconds, RelayTimeLimits limits)
    {
        Desk = desk;
        RandomSeed = randomSeed;
        CreatedAt = createdAt;
        NpcLoadSeconds = npcLoadSeconds;
        Limits = limits;
        Seats = desk.Humans.ToDictionary(human => human.Uid, human => new RelaySeat(human));
        Judges = desk.Judges.ToDictionary(judge => judge.Uid, judge => new RelaySeat(judge));
        StoppingToken = stopping.Token;
    }

    public BattleDesk Desk { get; }
    public int RandomSeed { get; }
    public long CreatedAt { get; }
    public RelayTimeLimits Limits { get; }
    public IReadOnlyDictionary<ulong, RelaySeat> Seats { get; }
    public IReadOnlyDictionary<ulong, RelaySeat> Judges { get; }
    public Lock Gate { get; } = new();
    public CancellationToken StoppingToken { get; }
    public RelayGamePhase Phase { get; private set; } = RelayGamePhase.Waiting;
    public long LoadStartTime { get; private set; }
    public long LoadStartedAt { get; private set; }
    public long GameStartTime { get; private set; }
    public long FightStartedAt { get; private set; }
    public long? FirstGameoverAt { get; set; }
    public long? AllLeftAt { get; set; }
    public bool StragglersNoticed { get; set; }
    public int LoadTick { get; set; } = NoLoadTick;
    public int KFrapsNo { get; private set; }
    public bool Finished => Phase == RelayGamePhase.Finished;
    public bool FrameStoreFull { get; private set; }
    public long StoredFrameBytes { get; private set; }
    public int? SurrenderWinCampClient { get; set; }
    public int FrapBootSession { get; set; }
    public int FrapBootSessionSince { get; set; }
    public Dictionary<int, int> Processes { get; } = [];
    public List<pbCSDT_FRAPBOOT_CC> PendingBootInfo { get; } = [];
    public List<ulong> GameoverOrder { get; } = [];
    public Dictionary<int, CampVote> Votes { get; } = [];
    public Dictionary<int, long> LastSurrenderTimes { get; } = [];
    public HashSet<int> CanopyShownCamps { get; } = [];
    public bool HangUpReported { get; set; }
    public bool EndedByJudge { get; set; }
    public RefereePause? Pause { get; set; }
    public TimeSpan PausedTime { get; set; }

    public IReadOnlyDictionary<int, int> NpcLoadSeconds { get; }

    public IEnumerable<RelaySeat> ConnectedSeats => Seats.Values.Where(seat => seat.IsConnected);

    public IEnumerable<RelaySeat> ConnectedJudges => Judges.Values.Where(judge => judge.IsConnected);

    public IEnumerable<RelaySeat> ConnectedViewers => ConnectedSeats.Concat(ConnectedJudges);

    public ulong[] ConnectedUids => [.. ConnectedViewers.Select(seat => seat.Uid)];

    public bool IsSingleAcnt => Seats.Count == 1 && Judges.Count == 0;

    public bool RunsFrameLoop => !IsSingleAcnt;

    public bool BroadcastsNpcProcess => Desk.DeskInfo.ManMachine != CpuMatch.ManMachine;

    public bool AllHumansAttached => Seats.Values.All(seat => seat.IsConnected);

    public bool AllHumansLoaded => Seats.Values.All(seat => seat.LoadFinished);

    public bool AnyHumanLoaded => Seats.Values.Any(seat => seat.LoadFinished);

    public bool AllNpcsLoaded => NpcLoadSeconds.Keys.All(objId => Processes.GetValueOrDefault(objId) >= FullProcess);

    public bool AllHumansLeft => !Seats.Values.Any(seat => seat.IsConnected);

    public bool AllPlayingHumansReported =>
        Seats.Values.Any(seat => seat.Gameover is not null)
        && Seats.Values.All(seat => seat.Gameover is not null || !seat.IsConnected);

    public IEnumerable<RelaySeat> Stragglers => ConnectedSeats.Where(seat => seat.Gameover is null);

    public RelaySeat? FindSeat(ulong uid) => Seats.GetValueOrDefault(uid);

    public RelaySeat? FindJudge(ulong uid) => Judges.GetValueOrDefault(uid);

    public RelaySeat? FindViewer(ulong uid) => FindSeat(uid) ?? FindJudge(uid);

    public int NpcProcessAt(int objId, int tick) =>
        (int)Math.Min(FullProcess, (long)FullProcess * tick / NpcLoadSeconds[objId]);

    public PB_ENUM_RECONN_RELAY_STATE_TYPE ReconnStateOf(RelaySeat seat) => Phase switch
    {
        RelayGamePhase.Waiting => PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateWait,
        RelayGamePhase.Loading when !seat.HasBeginLoad => PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateWait,
        RelayGamePhase.Loading => PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateLoading,
        RelayGamePhase.Fighting => PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateGameing,
        _ => PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateGameover,
    };

    public pbCSMultgameGameoverReq? FindAgreedGameover()
    {
        var reports = GameoverOrder
            .Select(uid => Seats[uid])
            .Where(seat => !seat.QuitByExitButton && seat.Gameover is not null)
            .Select(seat => seat.Gameover!)
            .ToList();
        if (reports.Count == 0)
        {
            return null;
        }

        var votes = reports.CountBy(report => report.BattleParam.WinCampClient).ToDictionary();
        var mostVotes = votes.Values.Max();
        return reports.First(report => votes[report.BattleParam.WinCampClient] == mostVotes);
    }

    public int TakeSvrPkgSeq() => nextSvrPkgSeq++;

    public void BeginLoading(long startedAt, long startTime)
    {
        lock (Gate)
        {
            Transition(RelayGamePhase.Waiting, RelayGamePhase.Loading);
            LoadStartedAt = startedAt;
            LoadStartTime = startTime;
        }
    }

    public void BeginFight(long startedAt, long startTime)
    {
        lock (Gate)
        {
            Transition(RelayGamePhase.Loading, RelayGamePhase.Fighting);
            FightStartedAt = startedAt;
            GameStartTime = startTime;
        }
    }

    public void EndFight()
    {
        lock (Gate)
        {
            Transition(RelayGamePhase.Fighting, RelayGamePhase.Over);
        }
    }

    public bool Stop()
    {
        lock (Gate)
        {
            if (Finished)
            {
                return false;
            }

            Phase = RelayGamePhase.Finished;
        }

        try
        {
            stopping.Cancel();
        }
        finally
        {
            stopping.Dispose();
        }

        return true;
    }

    private void Transition(RelayGamePhase expected, RelayGamePhase next)
    {
        if (Phase != expected)
        {
            throw new InvalidOperationException($"Cannot change relay game from {Phase} to {next}");
        }

        Phase = next;
    }

    public int? EnemyCampOf(int campClient) =>
        Desk.Players.Select(player => player.CampClient).Where(camp => camp != campClient).Order().Select(camp => (int?)camp).FirstOrDefault();

    public void EmitFrame(int session)
    {
        var info = new pbCSDT_FRAPBOOT_INFO();
        if (!FrameStoreFull)
        {
            info.BootInfo.Add(PendingBootInfo);
        }

        PendingBootInfo.Clear();
        var infoBuff = info.ToNoTagByteArray();
        if (StoredFrameBytes + infoBuff.Length > MaxStoredFrameBytes)
        {
            FrameStoreFull = true;
            infoBuff = new pbCSDT_FRAPBOOT_INFO().ToNoTagByteArray();
        }

        KFrapsNo++;
        frames.Add(new StoredFrame(TakeSvrPkgSeq(), session, infoBuff));
        StoredFrameBytes += infoBuff.Length;
    }

    public pbSCFrapBootSingleNtf? FindFrame(int kFrapsNo) =>
        FindStoredFrame(kFrapsNo) is { } frame
            ? new pbSCFrapBootSingleNtf { KFrapsNo = kFrapsNo, InfoBuff = ByteString.CopyFrom(frame.InfoBuff) }
            : null;

    public StoredFrame? FindStoredFrame(int kFrapsNo) =>
        kFrapsNo >= 1 && kFrapsNo <= frames.Count ? frames[kFrapsNo - 1] : null;

    public int PendingBytesOf(int objId) =>
        PendingBootInfo.Where(command => command.ObjID == objId).Sum(command => command.Buff.Length);

    public int PendingBytes => PendingBootInfo.Sum(command => command.Buff.Length);

    public IReadOnlyDictionary<ulong, long>? RecordHash(int kFrapsNo, ulong uid, long hash, int keptFrames)
    {
        var newestStale = KFrapsNo - keptFrames;
        foreach (var stale in hashesByKFrapsNo.Keys.Where(key => key <= newestStale).ToList())
        {
            hashesByKFrapsNo.Remove(stale);
        }

        if (kFrapsNo <= newestStale)
        {
            return null;
        }

        if (!hashesByKFrapsNo.TryGetValue(kFrapsNo, out var hashes))
        {
            hashes = [];
            hashesByKFrapsNo[kFrapsNo] = hashes;
        }

        hashes[uid] = hash;
        if (!ConnectedSeats.All(seat => hashes.ContainsKey(seat.Uid)) || hashes.Count < 2)
        {
            return null;
        }

        hashesByKFrapsNo.Remove(kFrapsNo);
        return hashes;
    }
}
