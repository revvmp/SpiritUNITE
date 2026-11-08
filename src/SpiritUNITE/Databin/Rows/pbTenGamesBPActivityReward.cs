namespace SpiritUNITE.Databin;

public sealed record pbTenGamesBPActivityReward(int ActivityID, int Seq, int WinCnt, int FreeDropID, int SenierDropID)
{
    public static long Key(int activityID, int seq) => ((long)activityID << 32) | (uint)seq;
}
