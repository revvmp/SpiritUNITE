namespace SpiritUNITE.Databin;

public sealed record pbTenGamesRankReward(
    int GamesId,
    int GearID,
    int StartRank,
    int EndRank,
    int RewardDropID,
    int IsParticipationAward = 0)
{
    public static long Key(int gamesId, int gearID) => ((long)gamesId << 32) | (uint)gearID;
}
