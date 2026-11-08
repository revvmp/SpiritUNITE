namespace SpiritUnite.Databin;

public sealed record pbDrop_Base(int DropID, int DropSeq, int DropPackID)
{
    public static long Key(int dropID, int dropSeq) => ((long)dropID << 32) | (uint)dropSeq;
}
