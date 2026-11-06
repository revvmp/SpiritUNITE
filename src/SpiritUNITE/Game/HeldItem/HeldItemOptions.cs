namespace SpiritUNITE.Game.HeldItem;

public sealed class HeldItemOptions
{
    public const string SectionName = "HeldItem";

    public int[] UnlockGrantItemIds { get; init; } = [];

    public bool IsValid() => UnlockGrantItemIds is not null && UnlockGrantItemIds.All(itemId => itemId > 0);
}
