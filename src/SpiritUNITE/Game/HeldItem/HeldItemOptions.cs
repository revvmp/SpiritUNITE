namespace SpiritUNITE.Game.HeldItem;

public sealed class HeldItemOptions
{
    public const string SectionName = "HeldItem";

    private static readonly int[] OfficialUnlockGrantItemIds = [1002001014];

    public int[]? UnlockGrantItemIds { get; init; }

    public IReadOnlyList<int> UnlockGrantItemIdsOrOfficial => UnlockGrantItemIds ?? OfficialUnlockGrantItemIds;

    public bool IsValid() => UnlockGrantItemIdsOrOfficial.All(itemId => itemId > 0);
}
