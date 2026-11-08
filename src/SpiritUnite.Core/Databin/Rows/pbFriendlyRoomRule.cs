namespace SpiritUnite.Databin;

public sealed record pbFriendlyRoomRule(
    int Id,
    IReadOnlyList<int> RelationOpen,
    IReadOnlyList<int> RelationClose);
