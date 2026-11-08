namespace SpiritUNITE.Databin;

public sealed record pbTenGames(
    int GamesId,
    long DisplayDate,
    long HideDate,
    long OpenDate,
    long CloseDate,
    IReadOnlyList<long> OpenGameTime,
    IReadOnlyList<long> CloseGameTime,
    long SettleEndTime,
    IReadOnlyList<int> CanApplyMember,
    IReadOnlyList<int> MapList,
    int JoinReward = 0);
