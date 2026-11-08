namespace SpiritUnite.Databin;

public sealed record pbRankedMatchSeasonTaskAward(
    int AwardGroupId,
    int SDropID,
    int BigRankedDivision = 0,
    int RankedDivision = 0,
    int IsRecvShow = 0,
    int HistoryDivisionCanAward = 0)
{
    public static long Key(int awardGroupId, int bigRankedDivision, int rankedDivision) =>
        ((long)awardGroupId << 40) | ((long)bigRankedDivision << 32) | (uint)rankedDivision;
}
