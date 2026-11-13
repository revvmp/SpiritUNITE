namespace SpiritUnite.Databin;

public sealed record pbShowSerialRewardActivity(int ActivityID, int Seq, int DropID)
{
    public static long Key(int activityID, int seq) => ((long)activityID << 32) | (uint)seq;
}
