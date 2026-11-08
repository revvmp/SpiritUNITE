namespace SpiritUnite.Packets.Messages;

public sealed partial class pbNewbieGuideSteps
{
    public static long Key(int newbieEnum, int stepIndex) =>
        ((long)stepIndex << 32) | (uint)newbieEnum;
}
