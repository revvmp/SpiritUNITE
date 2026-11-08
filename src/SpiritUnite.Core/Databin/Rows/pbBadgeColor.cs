namespace SpiritUnite.Databin;

public sealed record pbBadgeColor(int BadgeColor, int BadgeColorLv, int LvNeedCount = 0)
{
    public static long Key(int badgeColor, int badgeColorLv) => ((long)badgeColorLv << 32) | (uint)badgeColor;
}
