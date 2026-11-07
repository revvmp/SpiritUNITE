namespace SpiritUNITE.Packets.Messages;

public sealed partial class pbBattlePassLevel
{
    public static long Key(int seasonID, int level) =>
        ((long)seasonID << 32) | (uint)level;
}
