namespace SpiritUNITE.Packets.Messages;

public sealed partial class pbBattlePass_WeekIndex
{
    public static long Key(int seasonID, int weekIndex) =>
        ((long)seasonID << 32) | (uint)weekIndex;
}
