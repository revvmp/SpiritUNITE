namespace SpiritUnite.Packets.Messages;

public sealed partial class pbHangupScheme
{
    public static long Key(int hangupSchemeID, int keyID) =>
        ((long)keyID << 32) | (uint)hangupSchemeID;
}
