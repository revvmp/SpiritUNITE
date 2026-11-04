namespace SpiritUNITE.Packets.Messages;

public sealed partial class pbSystemUnlock
{
    public static long Key(int id, int schemeId) =>
        ((long)schemeId << 32) | (uint)id;
}
