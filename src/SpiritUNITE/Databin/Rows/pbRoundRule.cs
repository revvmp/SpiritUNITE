namespace SpiritUNITE.Databin;

public sealed record pbRoundRule(
    int RoundRuleId,
    IReadOnlyList<int> CampOnePos,
    IReadOnlyList<int> CampTwoPos,
    int BanOrPick = 0,
    int TimeOut = 0);
