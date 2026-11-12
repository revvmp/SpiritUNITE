namespace SpiritUnite.Databin;

public sealed record pbBattlePassLevelCycleReward(
    int SeasonID,
    int CycleLevel,
    int CommonAwardID = 0,
    int CommonAwardNum = 0,
    int EliteAwardID = 0,
    int EliteAwardNum = 0)
{
    public static long Key(int seasonID, int cycleLevel) => ((long)cycleLevel << 32) | (uint)seasonID;
}
