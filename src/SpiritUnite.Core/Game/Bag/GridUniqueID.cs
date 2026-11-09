namespace SpiritUnite.Game.Bag;

public static class GridUniqueID
{
    public const ulong None = 0;

    private const int UnixSecondsShift = 32;
    private const uint SequenceStep = 2;
    private const uint LastSequence = uint.MaxValue - SequenceStep;

    private static readonly Lock Gate = new();
    private static long allocatedSecond;
    private static uint allocatedSequence;

    public static void ContinueAfter(ulong lastAllocated)
    {
        lock (Gate)
        {
            allocatedSecond = (long)(lastAllocated >> UnixSecondsShift);
            allocatedSequence = (uint)lastAllocated;
        }
    }

    public static ulong Next(long unixSeconds)
    {
        lock (Gate)
        {
            if (unixSeconds > allocatedSecond)
            {
                allocatedSecond = unixSeconds;
                allocatedSequence = 0;
            }
            else if (allocatedSequence > LastSequence)
            {
                allocatedSecond++;
                allocatedSequence = 0;
            }

            allocatedSequence += SequenceStep;
            return ((ulong)allocatedSecond << UnixSecondsShift) | allocatedSequence;
        }
    }
}
