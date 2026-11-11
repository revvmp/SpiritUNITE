using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using SpiritUnite.Network.Gcp;

namespace SpiritUnite.Network.Lwip;

internal readonly record struct LwipFlowKey(ZsdId Id, ushort ClientPort);

internal enum LwipInboundKind
{
    Stream,
    UnreliableBody,
    UnreliablePacket,
    IdleClose,
}

internal readonly record struct LwipInbound(LwipInboundKind Kind, byte[] Data, bool Compressed);

internal sealed class LwipFlow
{
    private const int InitialRetransmitMs = 200;
    private const int MinRetransmitMs = 30;
    private const int MaxRetransmitMs = 1000;
    private const int RetransmitRttPercent = 130;
    private const int Percent = 100;
    private const int MaxRttSampleMs = 5000;
    private const int MaxBackoffShift = 5;
    private const int FastRetransmitDuplicateAcks = 2;
    private const int MaxOutOfOrderSegments = 128;
    private const int MaxInboundBacklog = 4096;
    private const int MaxPendingBytes = 4 * GcpHead.MaxPacketSize;
    private const int ReplayWindow = 64;
    private const int SackWordSize = sizeof(uint);
    private const long KeepaliveIntervalMs = 3000;
    private const long IdleCloseMs = 27800;
    private const long HandshakeTimeoutMs = 30000;
    private const uint UnreliableSeq = 0;
    private const uint CompressedSectionMask = 0xFF;
    private const LwipFlags SynAckFlags = LwipFlags.Syn | LwipFlags.Ack;
    private const LwipFlags UnreliableFlags = LwipFlags.Unreliable | LwipFlags.Ack;
    private const LwipFlags LastSegmentFlags = LwipFlags.Ack | LwipFlags.Psh;

    private readonly Lock gate = new();
    private readonly Socket socket;
    private readonly ushort serverPort;
    private readonly Action<LwipFlow> closed;
    private readonly Action<LwipFlow, long>? schedule;
    private readonly Channel<LwipInbound> inbound = Channel.CreateUnbounded<LwipInbound>();
    private readonly List<SentSegment> unacked = [];
    private readonly Queue<(byte[] Payload, LwipFlags Flags)> pending = [];
    private readonly Dictionary<uint, byte[]> outOfOrder = [];
    private readonly uint initialReceiveSeq;
    private readonly uint initialSendSeq;
    private readonly LwipFlags synAckFlags;
    private LwipFlowState state = LwipFlowState.SynReceived;
    private EndPoint remote;
    private uint sendUnacked;
    private uint sendNext;
    private uint receiveNext;
    private int pendingBytes;
    private int peerWindow = LwipEnvelope.Window;
    private int retransmitMs = InitialRetransmitMs;
    private int duplicateAcks;
    private long synAckSentAt;
    private int synAckRetries;
    private long lastReceivedAt;
    private long lastStreamReceivedAt;
    private long lastKeepaliveAt;
    private bool idleCloseRequested;
    private bool hasPeerClock;
    private ushort peerClock;
    private long peerClockArrivedAt;
    private uint highestCounter;
    private ulong seenCounters = 1;

    public LwipFlow(LwipFlowKey key, LwipTicket ticket, ushort serverPort, LwipSegment syn, uint counter, EndPoint remote, Socket socket, Action<LwipFlow> closed, Action<LwipFlow, long>? schedule = null)
    {
        Key = key;
        Ticket = ticket;
        this.serverPort = serverPort;
        this.remote = remote;
        this.socket = socket;
        this.closed = closed;
        this.schedule = schedule;
        highestCounter = counter;
        initialReceiveSeq = syn.Seq;
        initialSendSeq = BinaryPrimitives.ReadUInt32LittleEndian(RandomNumberGenerator.GetBytes(sizeof(uint)));
        synAckFlags = SynAckFlags | (syn.Flags & LwipFlags.CompressedUnreliable);
        receiveNext = syn.Seq + 1;
        sendUnacked = initialSendSeq;
        sendNext = initialSendSeq + 1;

        var now = Environment.TickCount64;
        lastReceivedAt = now;
        lastStreamReceivedAt = now;
        UpdateTimestamps(syn, now);
        SendSynAck(now);
    }

    private enum LwipFlowState
    {
        SynReceived,
        Established,
        Closed,
    }

    public LwipFlowKey Key { get; }

    public LwipTicket Ticket { get; }

    public ChannelReader<LwipInbound> Inbound => inbound.Reader;

    public bool IsClosed
    {
        get
        {
            lock (gate)
            {
                return state == LwipFlowState.Closed;
            }
        }
    }

    public EndPoint Remote
    {
        get
        {
            lock (gate)
            {
                return remote;
            }
        }
    }

    public bool IsReplacedBy(LwipSegment segment) =>
        segment.Has(LwipFlags.Syn) && !segment.Has(LwipFlags.Ack) && segment.Seq != initialReceiveSeq;

    public bool Receive(LwipSegment segment, uint counter, EndPoint source)
    {
        lock (gate)
        {
            if (state == LwipFlowState.Closed || !AcceptCounter(counter, source))
            {
                return false;
            }

            var now = Environment.TickCount64;
            lastReceivedAt = now;
            UpdateTimestamps(segment, now);
            if (segment.Has(LwipFlags.Rst))
            {
                CloseLocked();
            }
            else if (segment.Has(LwipFlags.Unreliable))
            {
                ReceiveUnreliable(segment);
            }
            else if (segment.Has(LwipFlags.Syn))
            {
                ReceiveRepeatedSyn(segment, now);
            }
            else if (CompletesHandshake(segment))
            {
                RecordStreamReceived(now);
                if (segment.Has(LwipFlags.Ack))
                {
                    ReceiveAck(segment, now);
                }

                ReceiveData(segment, now);
            }

            ScheduleMaintenance();
            return true;
        }
    }

    public Task WriteStreamAsync(byte[] data)
    {
        lock (gate)
        {
            if (state != LwipFlowState.Established)
            {
                return Task.FromException(new IOException("Relay flow is not established"));
            }

            if (!QueueStream(data))
            {
                return Task.FromException(new IOException("Relay flow send queue overflowed"));
            }

            TransmitPending(Environment.TickCount64);
            ScheduleMaintenance();
            return Task.CompletedTask;
        }
    }

    public Task WriteUnreliableAsync(byte[] packet)
    {
        lock (gate)
        {
            if (state != LwipFlowState.Established)
            {
                return Task.FromException(new IOException("Relay flow is not established"));
            }

            var now = Environment.TickCount64;
            if (packet.Length > LwipEnvelope.MaxSegmentSize)
            {
                if (!QueueStream(packet))
                {
                    return Task.FromException(new IOException("Relay flow send queue overflowed"));
                }

                TransmitPending(now);
                ScheduleMaintenance();
                return Task.CompletedTask;
            }

            Send(BuildSegment(UnreliableSeq, UnreliableFlags, packet, now));
            return Task.CompletedTask;
        }
    }

    public void Tick(long now)
    {
        lock (gate)
        {
            if (state == LwipFlowState.Closed)
            {
                return;
            }

            if (state == LwipFlowState.SynReceived)
            {
                TickHandshake(now);
                ScheduleMaintenance();
                return;
            }

            if (!TickKeepalive(now))
            {
                return;
            }

            for (var index = 0; index < unacked.Count; index++)
            {
                var segment = unacked[index];
                if (IsRetransmitDue(segment, now))
                {
                    Retransmit(segment, now);
                }
            }

            TransmitPending(now);
            ScheduleMaintenance();
        }
    }

    public void StartMaintenance()
    {
        lock (gate)
        {
            ScheduleMaintenance();
        }
    }

    private void ScheduleMaintenance()
    {
        if (state == LwipFlowState.Closed || schedule is null)
        {
            return;
        }

        var due = state == LwipFlowState.SynReceived
            ? Math.Min(lastReceivedAt + HandshakeTimeoutMs, synAckSentAt + Backoff(synAckRetries))
            : idleCloseRequested ? long.MaxValue
            : Math.Min(lastStreamReceivedAt + IdleCloseMs, Math.Max(lastStreamReceivedAt, lastKeepaliveAt) + KeepaliveIntervalMs);
        if (state == LwipFlowState.Established)
        {
            foreach (var segment in unacked)
            {
                due = Math.Min(due, segment.SentAt + (segment.Sacked ? MaxRetransmitMs : Backoff(segment.Retries)));
            }
        }

        schedule(this, due);
    }

    public void Close()
    {
        lock (gate)
        {
            CloseLocked();
        }
    }

    private void TickHandshake(long now)
    {
        if (now - lastReceivedAt >= HandshakeTimeoutMs)
        {
            CloseLocked();
        }
        else if (now - synAckSentAt >= Backoff(synAckRetries))
        {
            synAckRetries++;
            SendSynAck(now);
        }
    }

    private bool TickKeepalive(long now)
    {
        if (idleCloseRequested)
        {
            return true;
        }

        if (now - lastStreamReceivedAt >= IdleCloseMs)
        {
            idleCloseRequested = true;
            Enqueue(new LwipInbound(LwipInboundKind.IdleClose, [], Compressed: false));
            return state != LwipFlowState.Closed;
        }

        if (now - Math.Max(lastStreamReceivedAt, lastKeepaliveAt) < KeepaliveIntervalMs)
        {
            return true;
        }

        lastKeepaliveAt = now;
        Send(BuildSegment(sendNext - 1, LwipFlags.Ack, ReadOnlyMemory<byte>.Empty, now));
        return true;
    }

    private void RecordStreamReceived(long now)
    {
        lastStreamReceivedAt = now;
    }

    private static bool SeqBefore(uint left, uint right) => (int)(left - right) < 0;

    private static bool SeqAtOrBefore(uint left, uint right) => (int)(left - right) <= 0;

    private bool AcceptCounter(uint counter, EndPoint source)
    {
        var ahead = (int)(counter - highestCounter);
        if (ahead > 0)
        {
            seenCounters = ahead >= ReplayWindow ? 1 : (seenCounters << ahead) | 1;
            highestCounter = counter;
            remote = source;
            return true;
        }

        var age = -ahead;
        var seenBit = 1UL << age;
        if (age >= ReplayWindow || (seenCounters & seenBit) != 0)
        {
            return false;
        }

        seenCounters |= seenBit;
        return true;
    }

    private void UpdateTimestamps(LwipSegment segment, long now)
    {
        if (segment.TimestampClock != 0)
        {
            hasPeerClock = true;
            peerClock = segment.TimestampClock;
            peerClockArrivedAt = now;
        }

        if (segment.TimestampEcho == 0)
        {
            return;
        }

        var rtt = (ushort)((ushort)now - segment.TimestampEcho);
        if (rtt <= MaxRttSampleMs)
        {
            retransmitMs = Math.Clamp(rtt * RetransmitRttPercent / Percent, MinRetransmitMs, MaxRetransmitMs);
        }
    }

    private bool CompletesHandshake(LwipSegment segment)
    {
        if (state == LwipFlowState.Established)
        {
            return true;
        }

        if (!segment.Has(LwipFlags.Ack) || segment.Ack != initialSendSeq + 1)
        {
            return false;
        }

        state = LwipFlowState.Established;
        sendUnacked = segment.Ack;
        return true;
    }

    private void ReceiveRepeatedSyn(LwipSegment segment, long now)
    {
        if (state == LwipFlowState.SynReceived && !segment.Has(LwipFlags.Ack) && segment.Seq == initialReceiveSeq)
        {
            SendSynAck(now);
        }
    }

    private void ReceiveUnreliable(LwipSegment segment)
    {
        if (state != LwipFlowState.Established || segment.Payload.IsEmpty)
        {
            return;
        }

        var item = segment.Has(LwipFlags.CompressedUnreliable)
            ? new LwipInbound(LwipInboundKind.UnreliableBody, segment.Payload.ToArray(), (segment.Seq & CompressedSectionMask) == GcpMessages.DataCompressed)
            : new LwipInbound(LwipInboundKind.UnreliablePacket, segment.Payload.ToArray(), Compressed: false);
        Enqueue(item);
    }

    private void ReceiveAck(LwipSegment segment, long now)
    {
        peerWindow = segment.Window;
        if (SeqBefore(sendUnacked, segment.Ack) && SeqAtOrBefore(segment.Ack, sendNext))
        {
            sendUnacked = segment.Ack;
            unacked.RemoveAll(sent => SeqAtOrBefore(sent.End, segment.Ack));
            duplicateAcks = 0;
        }
        else if (segment.Ack == sendUnacked && segment.Payload.IsEmpty && unacked.Count > 0
            && ++duplicateAcks == FastRetransmitDuplicateAcks
            && unacked.FirstOrDefault(sent => !sent.Sacked) is { } first)
        {
            Retransmit(first, now);
        }

        if (segment.Payload.IsEmpty)
        {
            MarkSacked(segment.Options.Span);
        }

        TransmitPending(now);
    }

    private void MarkSacked(ReadOnlySpan<byte> options)
    {
        for (var offset = 0; offset + SackWordSize <= options.Length; offset += SackWordSize)
        {
            var heldSeq = BinaryPrimitives.ReadUInt32BigEndian(options[offset..]);
            foreach (var sent in unacked.Where(sent => sent.Seq == heldSeq))
            {
                sent.Sacked = true;
            }
        }
    }

    private void ReceiveData(LwipSegment segment, long now)
    {
        if (segment.Payload.IsEmpty && !segment.Has(LwipFlags.Fin))
        {
            if (segment.Seq == receiveNext - 1)
            {
                Send(BuildSegment(sendNext, LwipFlags.Ack, ReadOnlyMemory<byte>.Empty, now));
            }

            return;
        }

        StoreOrDeliver(segment.Seq, segment.Payload.Span);
        if (state == LwipFlowState.Closed)
        {
            return;
        }

        var finished = segment.Has(LwipFlags.Fin) && segment.Seq + (uint)segment.Payload.Length == receiveNext;
        if (finished)
        {
            receiveNext++;
        }

        Send(BuildSegment(sendNext, LwipFlags.Ack, ReadOnlyMemory<byte>.Empty, now));
        if (finished)
        {
            CloseLocked();
        }
    }

    private void StoreOrDeliver(uint seq, ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        if (SeqAtOrBefore(seq, receiveNext))
        {
            var alreadyReceived = (int)(receiveNext - seq);
            if (alreadyReceived < payload.Length)
            {
                Deliver(payload[alreadyReceived..]);
                DeliverHeldSegments();
            }

            return;
        }

        if (SeqBefore(seq, receiveNext + LwipEnvelope.Window) && outOfOrder.Count < MaxOutOfOrderSegments)
        {
            outOfOrder.TryAdd(seq, payload.ToArray());
        }
    }

    private void DeliverHeldSegments()
    {
        var delivered = true;
        while (delivered && outOfOrder.Count > 0 && state != LwipFlowState.Closed)
        {
            delivered = false;
            foreach (var (seq, payload) in outOfOrder.ToArray())
            {
                var end = seq + (uint)payload.Length;
                if (SeqAtOrBefore(end, receiveNext))
                {
                    outOfOrder.Remove(seq);
                }
                else if (SeqAtOrBefore(seq, receiveNext))
                {
                    outOfOrder.Remove(seq);
                    Deliver(payload.AsSpan((int)(receiveNext - seq)));
                    delivered = true;
                }
            }
        }
    }

    private void Deliver(ReadOnlySpan<byte> data)
    {
        receiveNext += (uint)data.Length;
        Enqueue(new LwipInbound(LwipInboundKind.Stream, data.ToArray(), Compressed: false));
    }

    private void Enqueue(LwipInbound item)
    {
        if (inbound.Reader.Count >= MaxInboundBacklog || !inbound.Writer.TryWrite(item))
        {
            CloseLocked();
        }
    }

    private bool QueueStream(byte[] data)
    {
        if (data.Length > MaxPendingBytes - pendingBytes)
        {
            CloseLocked();
            return false;
        }

        for (var offset = 0; offset < data.Length; offset += LwipEnvelope.MaxSegmentSize)
        {
            var length = Math.Min(LwipEnvelope.MaxSegmentSize, data.Length - offset);
            var flags = offset + length == data.Length ? LastSegmentFlags : LwipFlags.Ack;
            pending.Enqueue((data.AsSpan(offset, length).ToArray(), flags));
        }

        pendingBytes += data.Length;
        return true;
    }

    private void TransmitPending(long now)
    {
        while (pending.TryPeek(out var next) && (long)(sendNext - sendUnacked) + next.Payload.Length <= peerWindow)
        {
            pending.Dequeue();
            pendingBytes -= next.Payload.Length;
            var sent = new SentSegment(sendNext, next.Payload, next.Flags) { SentAt = now };
            sendNext += (uint)next.Payload.Length;
            unacked.Add(sent);
            Send(BuildSegment(sent.Seq, sent.Flags, sent.Payload, now));
        }
    }

    private void Retransmit(SentSegment sent, long now)
    {
        sent.SentAt = now;
        sent.Retries++;
        Send(BuildSegment(sent.Seq, sent.Flags, sent.Payload, now));
    }

    private bool IsRetransmitDue(SentSegment sent, long now) =>
        now - sent.SentAt >= (sent.Sacked ? MaxRetransmitMs : Backoff(sent.Retries));

    private int Backoff(int retries) => Math.Min(retransmitMs << Math.Min(retries, MaxBackoffShift), MaxRetransmitMs);

    private void SendSynAck(long now)
    {
        synAckSentAt = now;
        Send(BuildSegment(initialSendSeq, synAckFlags, ReadOnlyMemory<byte>.Empty, now));
    }

    private LwipSegment BuildSegment(uint seq, LwipFlags flags, ReadOnlyMemory<byte> payload, long now) => new(
        serverPort,
        Key.ClientPort,
        seq,
        receiveNext,
        flags,
        LwipEnvelope.Window,
        hasPeerClock ? (ushort)(peerClock + (now - peerClockArrivedAt)) : (ushort)0,
        (ushort)now,
        ReadOnlyMemory<byte>.Empty,
        payload);

    private void Send(LwipSegment segment)
    {
        try
        {
            socket.SendTo(LwipEnvelope.BuildSegment(segment), remote);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
            CloseLocked();
        }
    }

    private void CloseLocked()
    {
        if (state == LwipFlowState.Closed)
        {
            return;
        }

        state = LwipFlowState.Closed;
        unacked.Clear();
        pending.Clear();
        pendingBytes = 0;
        outOfOrder.Clear();
        inbound.Writer.TryComplete();
        closed(this);
    }

    private sealed class SentSegment(uint seq, byte[] payload, LwipFlags flags)
    {
        public uint Seq { get; } = seq;

        public byte[] Payload { get; } = payload;

        public LwipFlags Flags { get; } = flags;

        public uint End => Seq + (uint)Payload.Length;

        public long SentAt { get; set; }

        public int Retries { get; set; }

        public bool Sacked { get; set; }
    }
}
