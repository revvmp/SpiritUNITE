using SpiritUnite.Packets;
using SpiritUnite.Packets.Messages;

namespace SpiritUnite.Battle;

public sealed record ObserveVideoStage(long Offset, long End, bool EndsStream);

public sealed class ObserveVideo(RelayGame game)
{
    private const long StreamStart = 0;
    private const int BeginLoadPacketIndex = 0;
    private const int MinFrameMilliseconds = 1;

    private readonly Lock gate = new();
    private readonly MemoryStream stream = new();
    private readonly List<long> packetEnds = [];
    private int frameMilliseconds;

    public RelayGame Game { get; } = game;

    public BattleDesk Desk => Game.Desk;

    public int ObserveNum { get; set; }

    public bool IsFightingLive
    {
        get
        {
            lock (Game.Gate)
            {
                return !Game.Finished && Game.Phase == RelayGamePhase.Fighting && Game.RunsFrameLoop;
            }
        }
    }

    public bool IsOver
    {
        get
        {
            lock (Game.Gate)
            {
                return Game.Finished || Game.Phase == RelayGamePhase.Over;
            }
        }
    }

    public long GameStartTime
    {
        get
        {
            lock (Game.Gate)
            {
                return Game.GameStartTime;
            }
        }
    }

    public bool HasDelayedFrames(TimeSpan observeDelay)
    {
        lock (gate)
        {
            return ReadAvailableFrames(observeDelay).AvailableFrames > 0;
        }
    }

    public ObserveVideoStage? NextStage(long offset, TimeSpan observeDelay, out bool allOver)
    {
        lock (gate)
        {
            var (availableFrames, over) = ReadAvailableFrames(observeDelay);
            AppendFrames(availableFrames);
            var beginLoadEnd = packetEnds[BeginLoadPacketIndex];
            var end = offset < beginLoadEnd ? beginLoadEnd : packetEnds[availableFrames];
            allOver = over && offset >= beginLoadEnd && offset == packetEnds[^1];
            if (offset < StreamStart || offset >= end)
            {
                return null;
            }

            return new ObserveVideoStage(offset, end, over && offset >= beginLoadEnd && end == packetEnds[^1]);
        }
    }

    public byte[] Read(long offset, int length)
    {
        lock (gate)
        {
            var buffer = new byte[length];
            stream.Position = offset;
            stream.ReadExactly(buffer);
            return buffer;
        }
    }

    private (int AvailableFrames, bool Over) ReadAvailableFrames(TimeSpan observeDelay)
    {
        lock (Game.Gate)
        {
            if (packetEnds.Count == 0)
            {
                var beginLoad = RelayManager.BuildObserverBeginLoadNtf(Game);
                frameMilliseconds = Math.Max(MinFrameMilliseconds, beginLoad.KFrapsFreqMs);
                Append(GamePacket.Create(PB_ENUM_CS_MSGID_DEF.PbScidMultgameBeginLoadNtf, beginLoad).ToRelayByteArray());
            }

            var over = Game.Finished || Game.Phase == RelayGamePhase.Over;
            var delayFrames = (int)Math.Ceiling(observeDelay.TotalMilliseconds / frameMilliseconds);
            return (over ? Game.KFrapsNo : Math.Max(0, Game.KFrapsNo - delayFrames), over);
        }
    }

    private void AppendFrames(int availableFrames)
    {
        var builtFrames = packetEnds.Count - 1;
        if (builtFrames >= availableFrames)
        {
            return;
        }

        lock (Game.Gate)
        {
            for (var kFrapsNo = builtFrames + 1; kFrapsNo <= availableFrames; kFrapsNo++)
            {
                var stored = Game.FindStoredFrame(kFrapsNo)!;
                var ntf = new pbSCFrapBootInfoNtf { Session = stored.Session, SpareFrap = { Game.FindFrame(kFrapsNo)! } };
                var packet = new GamePacket((int)PB_ENUM_CS_MSGID_DEF.PbScidFrapBootInfoNtf, Uid: 0, ClientSeq: 0, ntf.ToNoTagByteArray())
                {
                    SvrPkgSeq = stored.SvrPkgSeq,
                    MsgIDInBodyOnly = true,
                };
                Append(packet.ToRelayByteArray());
            }
        }
    }

    private void Append(byte[] packet)
    {
        stream.Position = stream.Length;
        stream.Write(packet);
        packetEnds.Add(stream.Length);
    }
}
