namespace SpiritUnite.Databin;

public sealed record pbDrop_Pack(int DropPackID, int DropPackSeq, int ResID, int Weight, int MinDropCount, int MaxDropCount)
{
    public static long Key(int dropPackID, int dropPackSeq) => ((long)dropPackID << 32) | (uint)dropPackSeq;
}
