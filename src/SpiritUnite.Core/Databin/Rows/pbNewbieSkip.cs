namespace SpiritUnite.Databin;

public sealed record pbNewbieSkip(int SkipID, int NewbieEnum)
{
    public static long Key(int skipID, int newbieEnum) => ((long)newbieEnum << 32) | (uint)skipID;
}
