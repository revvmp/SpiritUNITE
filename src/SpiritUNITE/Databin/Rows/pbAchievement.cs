namespace SpiritUNITE.Databin;

public sealed record pbAchievement(
    int AchievementID,
    int AchievementTaskID,
    int AchievementType = 0,
    int Score = 0);
