namespace SpiritUnite.Packets.Messages;

public sealed partial class pbBattlePassLevelReward
{
    public static long Key(int seasonID, int level) =>
        ((long)seasonID << 32) | (uint)level;
}
