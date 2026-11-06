using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Battle;

public enum RelayGamePhase
{
    Loading,
    Fighting,
    Over,
}

public sealed class RelayGame(BattleDesk desk, int randomSeed, long loadStartTime, long loadStartedAt, IReadOnlyDictionary<int, int> npcLoadSeconds)
{
    public const int FullProcess = 1000;
    public const long MaxStoredFrameBytes = 16 * 1024 * 1024;

    private const long TrimmedFrameBytes = MaxStoredFrameBytes / 4 * 3;

    private readonly List<pbSCFrapBootSingleNtf> frames = [];
    private readonly List<int> frameBytes = [];
    private long storedFrameBytes;

    public BattleDesk Desk { get; } = desk;
    public int RandomSeed { get; } = randomSeed;
    public long LoadStartTime { get; } = loadStartTime;
    public long LoadStartedAt { get; } = loadStartedAt;
    public Lock Gate { get; } = new();
    public CancellationTokenSource Stopping { get; } = new();
    public RelayGamePhase Phase { get; set; } = RelayGamePhase.Loading;
    public long GameStartTime { get; set; }
    public long FightStartedAt { get; set; }
    public int LoadTick { get; set; }
    public int KFrapsNo { get; set; }
    public bool Finished { get; set; }
    public pbCSMultgameGameoverReq? AgreedGameover { get; set; }
    public HashSet<ulong> ConnectedUids { get; } = [];
    public Dictionary<int, int> Processes { get; } = [];
    public HashSet<ulong> LoadFinishedUids { get; } = [];
    public HashSet<ulong> GameoverUids { get; } = [];
    public List<pbCSDT_FRAPBOOT_CC> PendingBootInfo { get; } = [];
    public Dictionary<int, int> PendingBootBytesByObjId { get; } = [];
    public Dictionary<int, int> PendingBootCountByObjId { get; } = [];
    public int FirstStoredKFrapsNo { get; private set; } = 1;

    public IReadOnlyDictionary<int, int> NpcLoadSeconds { get; } = npcLoadSeconds;

    public bool IsSingleAcnt => Desk.Humans.Count() == 1;

    public bool RunsFrameLoop => !IsSingleAcnt;

    public bool AllHumansLoaded => Desk.Humans.All(human => LoadFinishedUids.Contains(human.Uid));

    public bool AllNpcsLoaded => NpcLoadSeconds.Keys.All(objId => Processes.GetValueOrDefault(objId) >= FullProcess);

    public bool AllHumansLeft => ConnectedUids.Count == 0;

    public bool AllHumansReportedOrLeft =>
        GameoverUids.Count > 0 && Desk.Humans.All(human => GameoverUids.Contains(human.Uid) || !ConnectedUids.Contains(human.Uid));

    public int NpcProcessAt(int objId, int tick) =>
        (int)Math.Min(FullProcess, (long)FullProcess * tick / NpcLoadSeconds[objId]);

    public PB_ENUM_RECONN_RELAY_STATE_TYPE ReconnState => Phase switch
    {
        RelayGamePhase.Loading => PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateLoading,
        RelayGamePhase.Fighting => PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateGameing,
        _ => PB_ENUM_RECONN_RELAY_STATE_TYPE.PbReconnRelayStateGameover,
    };

    public bool EndFightIfAllReported()
    {
        if (Phase != RelayGamePhase.Fighting || !AllHumansReportedOrLeft)
        {
            return false;
        }

        Phase = RelayGamePhase.Over;
        return true;
    }

    public void TakePendingBootInfo(pbCSDT_FRAPBOOT_INFO info)
    {
        info.BootInfo.Add(PendingBootInfo);
        PendingBootInfo.Clear();
        PendingBootBytesByObjId.Clear();
        PendingBootCountByObjId.Clear();
    }

    public void StoreFrame(pbSCFrapBootSingleNtf frame)
    {
        var size = frame.CalculateSize();
        frames.Add(frame);
        frameBytes.Add(size);
        storedFrameBytes += size;
        if (storedFrameBytes > MaxStoredFrameBytes)
        {
            TrimFrames();
        }
    }

    public pbSCFrapBootSingleNtf? FindFrame(int kFrapsNo)
    {
        var index = (long)kFrapsNo - FirstStoredKFrapsNo;
        return index >= 0 && index < frames.Count ? frames[(int)index] : null;
    }

    private void TrimFrames()
    {
        var dropped = 0;
        while (dropped < frames.Count && storedFrameBytes > TrimmedFrameBytes)
        {
            storedFrameBytes -= frameBytes[dropped];
            dropped++;
        }

        frames.RemoveRange(0, dropped);
        frameBytes.RemoveRange(0, dropped);
        FirstStoredKFrapsNo += dropped;
    }
}
