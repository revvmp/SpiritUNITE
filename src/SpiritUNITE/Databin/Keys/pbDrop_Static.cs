namespace SpiritUNITE.Packets.Messages;

public sealed partial class pbDrop_Static
{
    public static long Key(int sDropID, int sDropSeq) =>
        ((long)sDropID << 32) | (uint)sDropSeq;
}
