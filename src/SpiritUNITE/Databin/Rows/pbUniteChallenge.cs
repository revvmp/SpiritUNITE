namespace SpiritUNITE.Databin;

public sealed record pbUniteChallenge(
    int ID,
    int PeriodID,
    long OpenTime,
    long CloseTime,
    int ShowMapId,
    pbUniteChallengeWinReward[] WinReward);

public sealed record pbUniteChallengeWinReward(int DropId);
