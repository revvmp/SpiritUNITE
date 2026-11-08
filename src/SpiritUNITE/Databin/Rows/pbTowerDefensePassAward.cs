namespace SpiritUNITE.Databin;

public sealed record pbTowerDefensePassAward(int PeriodID, int SceneId, int SDropID)
{
    public static long Key(int sceneId, int periodID) => ((long)sceneId << 32) | (uint)periodID;
}
