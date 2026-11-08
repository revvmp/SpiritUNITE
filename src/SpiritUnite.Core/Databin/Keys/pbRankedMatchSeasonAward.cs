namespace SpiritUnite.Packets.Messages;

public sealed partial class pbRankedMatchSeasonAward
{
    public static long Key(int awardId, int bigRankedDivision) =>
        ((long)awardId << 32) | (uint)bigRankedDivision;
}
