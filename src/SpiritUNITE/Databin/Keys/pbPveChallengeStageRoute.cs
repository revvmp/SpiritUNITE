namespace SpiritUNITE.Packets.Messages;

public sealed partial class pbPveChallengeStageRoute
{
    public static long Key(int stageID, int nodeID, int difficultyLevel) =>
        ((long)(ushort)stageID << 32) | ((long)(ushort)difficultyLevel << 16) | (ushort)nodeID;
}
