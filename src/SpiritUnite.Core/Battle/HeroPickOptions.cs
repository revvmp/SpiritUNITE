namespace SpiritUnite.Battle;

public sealed class HeroPickOptions
{
    public const string SectionName = "HeroPick";
    public const int NoReleaseOrderCap = 0;

    public int NpcHeroMaxReleaseOrder { get; init; } = 26;

    public bool IsValid() => NpcHeroMaxReleaseOrder >= NoReleaseOrderCap;

    public bool AllowsNpcHero(int releaseOrder) =>
        NpcHeroMaxReleaseOrder == NoReleaseOrderCap || releaseOrder <= NpcHeroMaxReleaseOrder;
}
