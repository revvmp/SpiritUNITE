using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SpiritUnite.Network.Lwip;

internal sealed class LwipTicket(ZsdId id, ulong uid, int token, uint chain, uint codeMix)
{
    private readonly Lock gate = new();
    private bool hasCounter;
    private uint highestCounter;
    private long lastCounterAt;

    public ZsdId Id { get; } = id;

    public ulong Uid { get; } = uid;

    public int Token { get; } = token;

    public uint Chain { get; } = chain;

    public uint CodeMix { get; } = codeMix;

    public void RecordCounter(uint counter, long now)
    {
        lock (gate)
        {
            if (!hasCounter || LwipEnvelope.CounterAfter(counter, highestCounter))
            {
                highestCounter = counter;
            }

            hasCounter = true;
            lastCounterAt = now;
        }
    }

    public bool TryClaimNewFlow(uint synCounter, long now, long requiredQuietMs)
    {
        lock (gate)
        {
            if (hasCounter && !LwipEnvelope.CounterAfter(synCounter, highestCounter) && now - lastCounterAt < requiredQuietMs)
            {
                return false;
            }

            hasCounter = true;
            highestCounter = synCounter;
            lastCounterAt = now;
            return true;
        }
    }
}

public sealed class LwipTickets
{
    private const int HexCharsPerByte = 2;

    private readonly ConcurrentDictionary<ZsdId, LwipTicket> tickets = new();

    public string Issue(ulong uid, int token)
    {
        while (true)
        {
            var zsdCode = RandomNumberGenerator.GetBytes(LwipEnvelope.ZsdCodeSize);
            var ticket = new LwipTicket(
                LwipEnvelope.ZsdIdOf(zsdCode),
                uid,
                token,
                LwipEnvelope.ZsdChain(zsdCode),
                LwipEnvelope.ZsdCodeMix(zsdCode));
            if (tickets.TryAdd(ticket.Id, ticket))
            {
                return Convert.ToHexStringLower(zsdCode);
            }
        }
    }

    public bool Revoke(string zsdCode) =>
        TryParseZsdCode(zsdCode, out var id) && tickets.TryRemove(id, out _);

    internal LwipTicket? Find(ZsdId id) => tickets.GetValueOrDefault(id);

    internal bool IsIssued(LwipTicket ticket) => tickets.TryGetValue(ticket.Id, out var current) && ReferenceEquals(current, ticket);

    private static bool TryParseZsdCode(string zsdCode, out ZsdId id)
    {
        id = default;
        Span<byte> bytes = stackalloc byte[LwipEnvelope.ZsdCodeSize];
        if (zsdCode.Length != LwipEnvelope.ZsdCodeSize * HexCharsPerByte
            || Convert.FromHexString(zsdCode, bytes, out _, out var written) != OperationStatus.Done
            || written != LwipEnvelope.ZsdCodeSize)
        {
            return false;
        }

        id = LwipEnvelope.ZsdIdOf(bytes);
        return true;
    }
}
