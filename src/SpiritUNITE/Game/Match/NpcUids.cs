using SpiritUNITE.Packets.Messages;

namespace SpiritUNITE.Game.Match;

public sealed class NpcUids(IEnumerable<ulong> humanUids)
{
    private const ulong FirstNpcUid = 1;

    private readonly HashSet<ulong> takenUids = [.. humanUids];
    private ulong nextNpcUid = FirstNpcUid;

    public static NpcUids Besides(IEnumerable<pbCOMDT_ROOMMEMBER_DT> humans) =>
        new(humans.Select(member => member.MemberDetail.MemberOfAcnt.Uid));

    public ulong NextInOrder()
    {
        while (!takenUids.Add(nextNpcUid))
        {
            nextNpcUid++;
        }

        return nextNpcUid++;
    }

    public ulong NextRandom(int reservedInOrderCount)
    {
        ulong uid;
        do
        {
            uid = (ulong)Random.Shared.NextInt64(long.MaxValue);
        }
        while (uid <= (ulong)reservedInOrderCount || !takenUids.Add(uid));

        return uid;
    }
}
